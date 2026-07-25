using System.Diagnostics;
using System.Linq;
using System.Numerics;
using Content.Server._Mono.Projectiles.TargetGuided;
using Content.Server._Mono.Projectiles.TargetSeeking;
using Content.Server.Alert.Commands;
using Content.Shared._Mono.Radar;
using Content.Shared.Projectiles;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;

namespace Content.Server._Mono.Radar;

public sealed partial class RadarBlipSystem : EntitySystem
{
    [Dependency] private SharedTransformSystem _xform = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;

    // Pooled collections to avoid per-request heap churn
    private readonly List<BlipNetData> _tempBlipsCache = new();
    private readonly List<MissileVectorNetData> _tempMissileCache = new();
    private readonly List<HitscanNetData> _tempHitscansCache = new();
    private readonly List<EntityUid> _tempSourcesCache = new();
    private readonly List<BlipConfig> _tempPaletteCache = new();
    private readonly Dictionary<BlipConfig, ushort> _paletteIndex = new();
    private readonly Dictionary<ICommonSession, List<BlipNetData>> _cachedBliplist = new();
    private readonly Dictionary<ICommonSession, List<BlipNetData>> _sentBliplist = new();
    private readonly Dictionary<ICommonSession, List<BlipNetData>> _unionedBliplist = new();
    private readonly Dictionary<ICommonSession, List<EntityUid>> _sessionUpdateList = new();

    private static readonly float UpdateInterval = 0.5f;
    private float _lastUpdated = 0;


    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<RequestBlipsEvent>(OnBlipsRequested);
        SubscribeLocalEvent<RadarBlipComponent, ComponentShutdown>(OnBlipShutdown);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _lastUpdated += frameTime;

        if (UpdateInterval <= _lastUpdated)
            return;

        _lastUpdated -= UpdateInterval;

        // MAIN Update Method

        // Run once per interval.

        // Get all Sessions requesting blips (Method)

        // For each session, get all entities within RANGE of the target with EQE blip component (Method)

        // Return as Dictionary

        // COMPARE TO OLD CACHE (Method)

        // For static blips, a simple Union of both lists will do.

        // But for moving blips, we must calculate the expected updated position of blips by the velocity.

        // For new blips, send them to the client for client to cache.

        // For old blips, remove them from the Cache and send a removal event.

        // Use dictionary, for each Session, return list. Send indivdiual list per client.

        _sentBliplist.Clear();

        // Calculate new positions for all blips with velocity > 0 with frametime and velocity vector. Replace old positions with new.

        // Compare cached blip list with created bliplist

        // Output those that do not match.

        // For those that have a moving velocity, apply specialcase matching.

        //but then if a blip moves it might not update
        //need to check if new position is consistent with velocity
        //If it is, remove from _unionedBliplist.
        //Send the Union of _sentBlipList and _cachedBlipList to client. "_unionedBliplist"

        // Client should no longer remove blips on their own.

        // Send removal requests to blips that have left the client's range, as determined by blipcomponent.

    }

    private void OnBlipsRequested(RequestBlipsEvent ev, EntitySessionEventArgs args)
    {
        if (!TryGetEntity(ev.Radar, out var radarUid)
            || !TryComp<RadarConsoleComponent>(radarUid, out var radar)
        )
            return;

        var sourcesEv = new GetRadarSourcesEvent();
        RaiseLocalEvent(radarUid.Value, ref sourcesEv);


        _tempSourcesCache.Clear();
        if (sourcesEv.Sources != null)
            _tempSourcesCache.AddRange(sourcesEv.Sources);
        else
            _tempSourcesCache.Add(radarUid.Value);

        // Ensure that we do not duplicate our values or keys, since we only clear after update() is called.
        if (_sessionUpdateList.ContainsKey(args.SenderSession))
            _sessionUpdateList[args.SenderSession] = _tempSourcesCache;
        else
            _sessionUpdateList.Add(args.SenderSession, _tempSourcesCache);

        AssembleBlipsReport((EntityUid)radarUid, _tempSourcesCache, radar);
        AssembleHitscanReport((EntityUid)radarUid, _tempSourcesCache, radar);

        _sentBliplist.Add(args.SenderSession, _tempBlipsCache);

        // Combine the blips and hitscan lines
        var giveEv = new GiveBlipsEvent(_tempPaletteCache, _tempBlipsCache, _tempMissileCache, _tempHitscansCache);
        RaiseNetworkEvent(giveEv, args.SenderSession);

        _tempBlipsCache.Clear();
        _tempMissileCache.Clear();
        _tempHitscansCache.Clear();
        _tempSourcesCache.Clear();
        _tempPaletteCache.Clear();
        _paletteIndex.Clear();
    }

    private void OnBlipShutdown(EntityUid blipUid, RadarBlipComponent component, ComponentShutdown args)
    {
        var netBlipUid = GetNetEntity(blipUid);
        var removalEv = new BlipRemovalEvent(netBlipUid);
        RaiseNetworkEvent(removalEv);
    }

    private void AssembleBlipsReport(EntityUid uid, List<EntityUid> sources, RadarConsoleComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return;

        var radarXform = Transform(uid);
        var radarGrid = radarXform.GridUid;
        var radarMapId = radarXform.MapID;

        var blipQuery = EntityQueryEnumerator<RadarBlipComponent, TransformComponent, PhysicsComponent>();

        while (blipQuery.MoveNext(out var blipUid, out var blip, out var blipXform, out var blipPhysics))
        {
            if (!blip.Enabled
                || blipXform.MapID != radarMapId
                || !NearAnySources(_xform.GetWorldPosition(blipXform), sources, blip.MaxDistance)
            )
                continue;

            var blipGrid = blipXform.GridUid;

            if (blip.RequireNoGrid && blipGrid != null // if we want no grid but we are on a grid
                || !blip.VisibleFromOtherGrids && blipGrid != radarGrid // or if we don't want to be visible from other grids but we're on another grid
            )
                continue; // don't show this blip

            var netBlipUid = GetNetEntity(blipUid);

            var blipVelocity = _physics.GetMapLinearVelocity(blipUid, blipPhysics, blipXform);

            // due to PVS being a thing, things will break if we try to parent to not the map or a grid
            var coord = blipXform.Coordinates;
            if (blipXform.ParentUid != blipXform.MapUid && blipXform.ParentUid != blipGrid)
                coord = _xform.WithEntityId(coord, blipGrid ?? blipXform.MapUid!.Value);

            var gridCfg = (BlipConfig?)null;
            var rotation = _xform.GetWorldRotation(blipXform);

            // we're parented to either the map or a grid and this is relative velocity so account for grid movement
            if (blipGrid != null)
            {
                var gridXform = Transform(blipGrid.Value);
                if (TryComp<PhysicsComponent>(blipGrid.Value, out var gridBody)) // prevent log spam
                    blipVelocity -= _physics.GetLinearVelocity(blipGrid.Value, coord.Position, gridBody);
                // it's local-frame velocity so rotate it too
                blipVelocity = (-gridXform.LocalRotation).RotateVec(blipVelocity);
                // and also offset the rotation
                rotation -= gridXform.LocalRotation;
                // and hijack our shape if we want to
                gridCfg = blip.GridConfig;
            }

            var configIdx = GetOrAddConfig(blip.Config);
            ushort? gridConfigIdx = gridCfg is { } gridCf ? GetOrAddConfig(gridCf) : null;

            // ideally we would handle blips being culled by detection on server but detection grid culling is already clientside so might as well
            _tempBlipsCache.Add(new(netBlipUid,
                            GetNetCoordinates(coord),
                            blipVelocity,
                            rotation,
                            configIdx,
                            gridConfigIdx));
        }

        var missileQuery = EntityQueryEnumerator<TargetSeekingComponent, RadarBlipComponent, TransformComponent>();
        while (missileQuery.MoveNext(out var missile, out var seeker, out var missileBlip, out var missileBlipXform))
        {
            var netMissileUid = GetNetEntity(missile);
            var missileArc = MathHelper.DegreesToRadians(seeker.ScanArc);
            if (seeker.ArcLines)
                _tempMissileCache.Add(new(netMissileUid,
                    (float)(seeker.MaxSpeed * 0.2),
                    missileArc));
        }
        // Check for SACLOS/mouse-guided missiles to add their directional lines to the console.
        var saclosQuery = EntityQueryEnumerator<TargetGuidedComponent, RadarBlipComponent, TransformComponent>();
        while (saclosQuery.MoveNext(out var missile, out var seeker, out var missileBlip, out var missileBlipXform))
        {
            var netMissileUid = GetNetEntity(missile);
            if (seeker.RadarLines)
                _tempMissileCache.Add(new(netMissileUid,
                    (float)(seeker.CurrentSpeed * 0.2),
                    0));
        }
    }

    /// <summary>
    /// Gets or create palette index for blip config.
    /// </summary>
    private ushort GetOrAddConfig(BlipConfig config)
    {
        if (_paletteIndex.TryGetValue(config, out var index))
            return index;

        if (_tempPaletteCache.Count >= ushort.MaxValue)
        {
            Log.Error($"Blip config count overflow! Reached max {ushort.MaxValue}, but trying to add more.");
            return 0;
        }

        index = (ushort)_tempPaletteCache.Count;
        _tempPaletteCache.Add(config);
        _paletteIndex[config] = index;
        return index;
    }

    /// <summary>
    /// Assembles trajectory information for hitscan projectiles to be displayed on radar
    /// </summary>
    private void AssembleHitscanReport(EntityUid uid, List<EntityUid> sources, RadarConsoleComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return;

        var radarXform = Transform(uid);

        var hitscanQuery = EntityQueryEnumerator<HitscanRadarComponent>();

        while (hitscanQuery.MoveNext(out var hitscanUid, out var hitscan))
        {
            if (!hitscan.Enabled)
                continue;

            if (!NearAnySources(hitscan.StartPosition, sources, component.MaxRange) && !NearAnySources(hitscan.EndPosition, sources, component.MaxRange))
                continue;

            _tempHitscansCache.Add(new(hitscan.StartPosition, hitscan.EndPosition, hitscan.LineThickness, hitscan.RadarColor));
        }
    }

    private bool NearAnySources(Vector2 coord, List<EntityUid> sources, float range)
    {
        var rsqr = range * range;
        foreach (var source in sources)
        {
            var pos = _xform.GetWorldPosition(source);
            if ((pos - coord).LengthSquared() < rsqr)
                return true;
        }
        return false;
    }
}
