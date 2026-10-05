using NavyThunder.Core.Mathematics;
using NavyThunder.Core.Ships;

namespace NavyThunder.Core.Commands;

/// <summary>
/// P05-1 (PHASE_05, PROJECT_DESIGN §11): structured gameplay commands. Godot input and
/// the naval AI state INTENT through these records; <c>BattleRunner.Submit</c> applies
/// them — the frontend never writes Ship fields or builds combat lambdas directly.
/// Commands are pure data (no engine types), so player and AI share one interface
/// (PROJECT_DESIGN §3.2-3) and a scripted sequence replays deterministically.
/// </summary>
public abstract record GameplayCommand;

/// <summary>Absolute helm orders; null members leave the current value untouched.</summary>
public sealed record HelmCommand(double? Throttle = null, double? Rudder = null) : GameplayCommand;

/// <summary>
/// Auto fire-control tier: engage a target with the ship's whole main battery. The
/// runner resolves the target id into the live tracking lambdas — the command stays a
/// plain id so a scripted sequence serializes and replays.
/// </summary>
public sealed record GunEngageCommand(string TargetId) : GameplayCommand;

/// <summary>
/// Manual laying (P05-2 manual tier): fire at a world-space aim point the caller keeps
/// refreshing (cursor range/bearing). An empty target id means unguided water point.
/// </summary>
public sealed record GunManualAimCommand(Vec3 AimPoint) : GameplayCommand;

/// <summary>Hold fire across the whole main battery.</summary>
public sealed record GunCeaseFireCommand : GameplayCommand;

/// <summary>Crew shell selection for the main battery (true = HE, false = data AP).</summary>
public sealed record ShellSelectCommand(bool HighExplosive) : GameplayCommand;

/// <summary>Damage-control orders for the player ship (P05-5): mode, flow priority
/// order, and — in manual mode — which single flow runs. Null members keep current.</summary>
public sealed record DcOrderCommand(
    DcMode? Mode = null,
    IReadOnlyList<DcFlow>? Priority = null,
    DcFlow? ManualFlow = null) : GameplayCommand;

/// <summary>Lock a target (id) for HUD/tactical-map markers and persisted engagement;
/// null clears the lock. Aiming intent still travels through the gun commands.</summary>
public sealed record TargetAssignCommand(string? TargetId) : GameplayCommand;
