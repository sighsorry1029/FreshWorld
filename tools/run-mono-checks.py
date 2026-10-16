"""Run a managed check executable in Valheim's embedded Mono, without launching Unity.

Requires Windows x64 Python. Arguments: Managed directory, Mono DLL, test EXE,
then the arguments for that test executable. Runs only trusted local test code.
"""
import ctypes
import os
import sys
from pathlib import Path


def main():
    if len(sys.argv) < 4:
        raise SystemExit("Usage: run-mono-checks.py <Managed> <mono-2.0-bdwgc.dll> <test.exe> [test arguments]")
    managed, runtime, executable = (Path(value).resolve(strict=True) for value in sys.argv[1:4])
    if os.name != "nt" or ctypes.sizeof(ctypes.c_void_p) != 8:
        raise SystemExit("Use Windows x64 Python with the game's x64 Mono runtime.")
    os.environ["MONO_PATH"] = str(executable.parent) + os.pathsep + str(managed)
    with os.add_dll_directory(str(runtime.parent)):
        mono = ctypes.CDLL(str(runtime))

        def api(name, result, params):
            function = getattr(mono, name)
            function.restype, function.argtypes = result, params
            return function

        api("mono_set_dirs", None, [ctypes.c_char_p, ctypes.c_char_p])(
            str(managed).encode(), str(runtime.parent.parent / "etc").encode())
        api("mono_config_parse", None, [ctypes.c_char_p])(None)
        domain = api("mono_jit_init_version", ctypes.c_void_p, [ctypes.c_char_p, ctypes.c_char_p])(
            b"FreshWorldTests", b"v4.0.30319")
        if not domain:
            raise RuntimeError("Mono domain initialization failed.")
        try:
            assembly = api("mono_domain_assembly_open", ctypes.c_void_p, [ctypes.c_void_p, ctypes.c_char_p])(
                domain, str(executable).encode())
            if not assembly:
                raise RuntimeError("Mono could not load the test executable.")
            arguments = [str(executable), *sys.argv[4:]]
            argv = (ctypes.c_char_p * len(arguments))(*(value.encode() for value in arguments))
            return api("mono_jit_exec", ctypes.c_int,
                       [ctypes.c_void_p, ctypes.c_void_p, ctypes.c_int, ctypes.POINTER(ctypes.c_char_p)])(
                domain, assembly, len(argv), argv)
        finally:
            api("mono_jit_cleanup", None, [ctypes.c_void_p])(domain)


if __name__ == "__main__":
    sys.exit(main())
