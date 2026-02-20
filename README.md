# KSA_XR

[OpenXR 1.0](https://www.khronos.org/openxr/) support for [Kittens Space Agency](https://ahwoo.com/app/100000/kitten-space-agency)

This mod is intended to support any OpenXR compatible system implementing the HMD Form Factor, with a primary stereo view.


This mod is written against the [StarMap loader](https://github.com/StarMapLoader/StarMap)

## Reqs
- Visual Studio 2026
- .net10
- OpenXR 1.0 compliant runtime and hardware

## Dependencies
Managed by NuGet

- Lib.Harmony
- OpenXR.Loader
- OpenXR.NET
- StarMap.API (From their GitHub NuGet repo)

## Development
For ease of development, this repository has a few convenience things:

- The `.csproj` file references KSA's installation folder at the default multi-user configuration (`C:\Program Files\Kitten Space Agency\`)
- `make_mod_junction.bat` is a script that will go (re)create a directoyr junction inside the Contents directory with the **64bit debug ouptut folder**
- The `.csproj` file implements a build step that copies all the necessary DLLs into thsi folde from the dependancies. This effectively allows to run the mod directly from Visual Studio
- A debug launch option will start StarMap.exe in the debugger.

