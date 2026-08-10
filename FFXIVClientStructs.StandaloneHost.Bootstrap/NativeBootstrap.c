#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>
#include <wchar.h>

typedef void* hostfxr_handle;
typedef int32_t (__cdecl *hostfxr_initialize_fn)(const wchar_t*, const void*, hostfxr_handle*);
typedef int32_t (__cdecl *hostfxr_get_runtime_delegate_fn)(hostfxr_handle, int32_t, void**);
typedef int32_t (__cdecl *hostfxr_close_fn)(hostfxr_handle);
typedef int32_t (__stdcall *load_assembly_bytes_fn)(const void*, size_t, const void*, size_t, void*, void*);
typedef int32_t (__stdcall *get_function_pointer_fn)(const wchar_t*, const wchar_t*, const wchar_t*, void*, void*, void**);
typedef int32_t (__stdcall *managed_entry_fn)(void*);

static HMODULE bootstrap_module;

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID reserved)
{
    (void)reserved;
    if (reason == DLL_PROCESS_ATTACH)
        bootstrap_module = instance;

    return TRUE;
}

static wchar_t* read_string(wchar_t** cursor)
{
    wchar_t* value = *cursor;
    *cursor += lstrlenW(value) + 1;
    return value;
}

static void write_error(const wchar_t* path, const char* stage, int32_t result)
{
    char message[160];
    int length = snprintf(message, sizeof(message), "%s failed with 0x%08X.\r\n", stage, (uint32_t)result);
    if (length <= 0)
        return;

    HANDLE file = CreateFileW(path, GENERIC_WRITE, FILE_SHARE_READ, NULL, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
    if (file == INVALID_HANDLE_VALUE)
        return;

    DWORD written;
    WriteFile(file, message, (DWORD)length, &written, NULL);
    CloseHandle(file);
}

static void* read_file(const wchar_t* path, size_t* length)
{
    HANDLE file = CreateFileW
    (
        path,
        GENERIC_READ,
        FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
        NULL,
        OPEN_EXISTING,
        FILE_ATTRIBUTE_NORMAL,
        NULL
    );
    if (file == INVALID_HANDLE_VALUE)
        return NULL;

    LARGE_INTEGER file_size;
    if (!GetFileSizeEx(file, &file_size) || file_size.QuadPart <= 0 || file_size.QuadPart > UINT32_MAX)
    {
        CloseHandle(file);
        return NULL;
    }

    void* content = HeapAlloc(GetProcessHeap(), 0, (size_t)file_size.QuadPart);
    if (content == NULL)
    {
        CloseHandle(file);
        return NULL;
    }

    DWORD bytes_read;
    if (!ReadFile(file, content, (DWORD)file_size.QuadPart, &bytes_read, NULL) || bytes_read != (DWORD)file_size.QuadPart)
    {
        HeapFree(GetProcessHeap(), 0, content);
        CloseHandle(file);
        return NULL;
    }

    CloseHandle(file);
    *length = (size_t)file_size.QuadPart;
    return content;
}

static wchar_t* get_parent_directory(const wchar_t* path)
{
    size_t length = wcslen(path);
    wchar_t* directory = GlobalAlloc(GPTR, (length + 1) * sizeof(wchar_t));
    if (directory == NULL)
        return NULL;

    memcpy(directory, path, (length + 1) * sizeof(wchar_t));
    while (length > 0)
    {
        length--;
        if (directory[length] == L'\\' || directory[length] == L'/')
        {
            directory[length] = L'\0';
            return directory;
        }
    }

    GlobalFree(directory);
    return NULL;
}

static void schedule_bootstrap_cleanup(const wchar_t* operation_directory)
{
    wchar_t bootstrap_path[32768];
    DWORD bootstrap_length = GetModuleFileNameW(bootstrap_module, bootstrap_path, ARRAYSIZE(bootstrap_path));
    if (bootstrap_length == 0 || bootstrap_length == ARRAYSIZE(bootstrap_path))
        return;

    size_t directory_length = operation_directory == NULL ? 0 : wcslen(operation_directory);
    wchar_t* operations_root = operation_directory == NULL ? NULL : get_parent_directory(operation_directory);
    size_t root_length = operations_root == NULL ? 0 : wcslen(operations_root);
    size_t allocation_length = bootstrap_length + 1 + directory_length + 1 + root_length + 1;
    wchar_t* paths = GlobalAlloc(GPTR, allocation_length * sizeof(wchar_t));
    if (paths == NULL)
    {
        if (operations_root != NULL)
            GlobalFree(operations_root);
        return;
    }

    wchar_t* directory = paths + bootstrap_length + 1;
    wchar_t* root = directory + directory_length + 1;
    memcpy(paths, bootstrap_path, (bootstrap_length + 1) * sizeof(wchar_t));
    if (operation_directory != NULL)
        memcpy(directory, operation_directory, (directory_length + 1) * sizeof(wchar_t));
    if (operations_root != NULL)
    {
        memcpy(root, operations_root, (root_length + 1) * sizeof(wchar_t));
        GlobalFree(operations_root);
    }

    HANDLE cleanup_thread = CreateThread
    (
        NULL,
        0,
        (LPTHREAD_START_ROUTINE)ExitThread,
        NULL,
        CREATE_SUSPENDED,
        NULL
    );
    if (cleanup_thread == NULL)
    {
        GlobalFree(paths);
        return;
    }

    BOOL queued = QueueUserAPC((PAPCFUNC)Sleep, cleanup_thread, 50) != 0 &&
                  QueueUserAPC((PAPCFUNC)DeleteFileW, cleanup_thread, (ULONG_PTR)paths) != 0;
    if (queued && operation_directory != NULL)
        queued = QueueUserAPC((PAPCFUNC)RemoveDirectoryW, cleanup_thread, (ULONG_PTR)directory) != 0;
    if (queued && root_length > 0)
        queued = QueueUserAPC((PAPCFUNC)RemoveDirectoryW, cleanup_thread, (ULONG_PTR)root) != 0;
    if (queued)
        queued = QueueUserAPC((PAPCFUNC)GlobalFree, cleanup_thread, (ULONG_PTR)paths) != 0;

    if (!queued || ResumeThread(cleanup_thread) == (DWORD)-1)
    {
        TerminateThread(cleanup_thread, 0);
        CloseHandle(cleanup_thread);
        GlobalFree(paths);
        return;
    }

    CloseHandle(cleanup_thread);
}

static __declspec(noreturn) void finish
(
    void* request_address,
    HANDLE caller_process,
    const wchar_t* runtime_config_path,
    const wchar_t* loader_assembly_path,
    const wchar_t* error_path,
    const wchar_t* output_path,
    DWORD result
)
{
    BOOL caller_exited = caller_process != NULL && WaitForSingleObject(caller_process, 0) == WAIT_OBJECT_0;
    wchar_t* operation_directory = get_parent_directory(loader_assembly_path);

    DeleteFileW(loader_assembly_path);
    DeleteFileW(runtime_config_path);

    if (caller_exited)
    {
        DeleteFileW(output_path);
        DeleteFileW(error_path);
        wchar_t* error_directory = get_parent_directory(output_path);
        if (error_directory != NULL)
        {
            RemoveDirectoryW(error_directory);
            GlobalFree(error_directory);
        }
    }

    schedule_bootstrap_cleanup(operation_directory);
    if (operation_directory != NULL)
        GlobalFree(operation_directory);

    if (caller_process != NULL)
        CloseHandle(caller_process);

    VirtualFree(request_address, 0, MEM_RELEASE);
    FreeLibraryAndExitThread(bootstrap_module, result);
}

__declspec(dllexport) DWORD __stdcall FFXIVClientStructsStandaloneHostBootstrap(void* request_address)
{
    wchar_t* cursor = request_address;
    const wchar_t* hostfxr_path = read_string(&cursor);
    const wchar_t* runtime_config_path = read_string(&cursor);
    const wchar_t* loader_assembly_path = read_string(&cursor);
    const wchar_t* loader_type_name = read_string(&cursor);
    read_string(&cursor);
    read_string(&cursor);
    const wchar_t* error_path = read_string(&cursor);
    const wchar_t* output_path = read_string(&cursor);
    const wchar_t* caller_process_id = read_string(&cursor);
    wchar_t* caller_process_handle = read_string(&cursor);

    DWORD result = ERROR_SUCCESS;
    DWORD caller_id = wcstoul(caller_process_id, NULL, 10);
    HANDLE caller_process = OpenProcess(SYNCHRONIZE, FALSE, caller_id);
    HMODULE hostfxr = NULL;
    hostfxr_handle context = NULL;
    hostfxr_close_fn close_host = NULL;

    if (caller_process == NULL)
    {
        result = HRESULT_FROM_WIN32(GetLastError());
        write_error(error_path, "caller process opening", (int32_t)result);
        goto complete;
    }

    _snwprintf_s
    (
        caller_process_handle,
        17,
        _TRUNCATE,
        L"%016llX",
        (unsigned long long)(uintptr_t)caller_process
    );

    hostfxr = LoadLibraryW(hostfxr_path);
    if (hostfxr == NULL)
    {
        result = HRESULT_FROM_WIN32(GetLastError());
        write_error(error_path, "hostfxr loading", (int32_t)result);
        goto complete;
    }

    hostfxr_initialize_fn initialize = (hostfxr_initialize_fn)GetProcAddress(hostfxr, "hostfxr_initialize_for_runtime_config");
    hostfxr_get_runtime_delegate_fn get_runtime_delegate =
        (hostfxr_get_runtime_delegate_fn)GetProcAddress(hostfxr, "hostfxr_get_runtime_delegate");
    close_host = (hostfxr_close_fn)GetProcAddress(hostfxr, "hostfxr_close");
    if (initialize == NULL || get_runtime_delegate == NULL || close_host == NULL)
    {
        result = HRESULT_FROM_WIN32(ERROR_PROC_NOT_FOUND);
        write_error(error_path, "hostfxr export lookup", (int32_t)result);
        goto complete;
    }

    int32_t host_result = initialize(runtime_config_path, NULL, &context);
    if (host_result < 0 || context == NULL)
    {
        result = (DWORD)host_result;
        write_error(error_path, "hostfxr initialization", host_result);
        goto complete;
    }

    void* load_assembly_bytes_address = NULL;
    host_result = get_runtime_delegate(context, 8, &load_assembly_bytes_address);
    if (host_result != 0 || load_assembly_bytes_address == NULL)
    {
        result = (DWORD)host_result;
        write_error(error_path, "load_assembly_bytes delegate lookup", host_result);
        goto complete;
    }

    void* get_function_pointer_address = NULL;
    host_result = get_runtime_delegate(context, 6, &get_function_pointer_address);
    if (host_result != 0 || get_function_pointer_address == NULL)
    {
        result = (DWORD)host_result;
        write_error(error_path, "get_function_pointer delegate lookup", host_result);
        goto complete;
    }

    size_t loader_length = 0;
    void* loader_content = read_file(loader_assembly_path, &loader_length);
    if (loader_content == NULL)
    {
        result = HRESULT_FROM_WIN32(GetLastError());
        write_error(error_path, "loader reading", (int32_t)result);
        goto complete;
    }

    load_assembly_bytes_fn load_assembly_bytes = (load_assembly_bytes_fn)load_assembly_bytes_address;
    int32_t load_result = load_assembly_bytes(loader_content, loader_length, NULL, 0, NULL, NULL);
    HeapFree(GetProcessHeap(), 0, loader_content);
    if (load_result != 0)
    {
        result = (DWORD)load_result;
        write_error(error_path, "managed bootstrap loading", load_result);
        goto complete;
    }

    get_function_pointer_fn get_function_pointer = (get_function_pointer_fn)get_function_pointer_address;
    void* entry_point = NULL;
    host_result = get_function_pointer
    (
        loader_type_name,
        L"Run",
        (const wchar_t*)-1,
        NULL,
        NULL,
        &entry_point
    );
    if (host_result != 0 || entry_point == NULL)
    {
        result = (DWORD)host_result;
        write_error(error_path, "managed bootstrap lookup", host_result);
        goto complete;
    }

    result = (DWORD)((managed_entry_fn)entry_point)(request_address);

complete:
    if (context != NULL && close_host != NULL)
        close_host(context);
    if (hostfxr != NULL)
        FreeLibrary(hostfxr);

    finish
    (
        request_address,
        caller_process,
        runtime_config_path,
        loader_assembly_path,
        error_path,
        output_path,
        result
    );
}
