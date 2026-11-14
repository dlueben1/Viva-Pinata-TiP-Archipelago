#pragma once

// Phase 2: .NET 10 native hosting from the VivaAP.Bridge native DLL.
// Official hosting APIs only: nethost (get_hostfxr_path) -> hostfxr
// (hostfxr_initialize_for_runtime_config, hdt_load_assembly_and_get_function_pointer)
// -> VivaAP.Client.dll [UnmanagedCallersOnly] entry point.

struct RexFabricApi;
struct RexFabricMod;

namespace vivaap
{
    // Starts the CLR using the mod's managed\ folder and invokes one managed
    // sign-of-life method. Never throws, never terminates the host process:
    // every stage (success or failure) is logged through RexFabric and
    // Visual Studio debugger output.
    //
    // Returns true only when the managed entry point executed and returned
    // the expected success token. A false return must not fail the mod.
    bool TryStartManagedRuntime(const RexFabricApi* api, RexFabricMod* mod);
}
