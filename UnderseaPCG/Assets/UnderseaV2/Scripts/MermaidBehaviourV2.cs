using UnityEngine;
using UnityMovementAI;
using NPBehave;
using Random = UnityEngine.Random;

// Behaviour for the mermaid in the UnderseaV2 scene. It takes over from MermaidBTController
// (wander / chase the loot) and adds idle activities, reactions to sharks, and a procedural
// pose (pitch, banking, rolls, somersaults) on top of the two animation clips the model has.
//
// The root object only ever yaws (UnityMovementAI works that way and faces along +X);
// everything else is applied to the visual child that holds the Animator.
[RequireComponent(typeof(MermaidMoveUnit))]
public class MermaidBehaviourV2 : MonoBehaviour
{
    public enum Activity { Cruise, Rest, Play, Surface, SchoolDance, VisitDiver, SeabedGlide }
    enum MoveMode { Wander, MoveTo, Orbit, Hover, Flee }

    [Header("Speeds (m/s)")]
    public float cruiseSpeed = 2.2f;
    public float playSpeed = 3.2f;
    public float chaseSpeed = 3.5f;
    public float fleeSpeed = 6f;

    [Header("Senses")]
    public float lootRadius = 8f;
    [Tooltip("The loot runs away when the mermaid gets this close, which counts as catching it.")]
    public float lootCatchDistance = 3.3f;
    public float sharkAlarmRadius = 6f;
    public float sharkSafeRadius = 11f;

    [Header("Space")]
    [Tooltip("Half size of the square area around the terrain origin she stays in.")]
    public float areaHalfSize = 26f;
    public float surfaceMargin = 1.5f;
    public float floorClearance = 1.6f;
    public LayerMask groundMask = 1;

    [Header("Pose")]
    public float maxPitch = 55f;
    public float maxBank = 35f;
    public float bobAmount = 0.12f;

    [Header("Idle activities")]
    [Tooltip("Relative chance of each Activity, in enum order.")]
    public float[] weights = { 3f, 2f, 2f, 1.5f, 2f, 1.5f, 1.5f };

    // What she is doing right now, for the UI and for debugging.
    public string State { get; private set; } = "Start";
    public Activity CurrentActivity { get; private set; } = Activity.Cruise;

    MermaidMoveUnit unit;
    MovementAIRigidbody rb;
    SteeringBasics steering;
    WallAvoidance wallAvoidance;
    Animator animator;
    Transform visual;
    Quaternion baseRotation;
    Vector3 basePosition;

    UnderwaterEnvironment environment;
    FishSchoolSystem school;
    Transform loot, diver;
    Vector3 areaCentre;
    Root tree;

    // Motor state, set by the behaviour tree.
    MoveMode mode = MoveMode.Wander;
    float speed;
    System.Func<Vector3> targetFn;      // MoveTo target, Orbit centre, Flee threat
    System.Func<Vector3> lookFn;        // what to face while hovering
    float wanderAngle, targetHeight;
    bool hugFloor;
    float orbitRadius, orbitDir = 1f, orbitHeight, orbitClimb;
    Vector3 lastCentre; bool hasLastCentre;
    float posePitch;
    bool lookAround;
    float floorY = float.NegativeInfinity, nextFloorCheck;

    // Senses.
    float lootDistance = float.MaxValue, sharkDistance = float.MaxValue;
    Vector3 sharkPosition, schoolCentre;
    float ignoreLootUntil, calmUntil, nextLookup;
    int forced = -1;
    Activity lastActivity = Activity.Rest;

    // Pose state.
    float pitch, bank, lastYaw, seed;
    float rollTime = 1f, rollDuration = 1f, rollTurns;
    float flipTime = 1f, flipDuration = 1f;

    float WaterLevel => environment != null ? environment.WaterLevel : 11f;
    float Ceiling => WaterLevel - surfaceMargin;
    bool HasFloor => !float.IsNegativeInfinity(floorY);

    void Awake()
    {
        unit = GetComponent<MermaidMoveUnit>();
        unit.externalControl = true;
        var oldBrain = GetComponent<MermaidBTController>();
        if (oldBrain != null) oldBrain.enabled = false;

        rb = GetComponent<MovementAIRigidbody>();
        steering = GetComponent<SteeringBasics>();
        wallAvoidance = GetComponent<WallAvoidance>();
        steering.turnSpeed = 6f;
        seed = Random.value * 100f;
    }

    void Start()
    {
        animator = unit.animator != null ? unit.animator : GetComponentInChildren<Animator>();
        if (animator != null)
        {
            visual = animator.transform;
            baseRotation = visual.localRotation;
            basePosition = visual.localPosition;
        }
        environment = FindAnyObjectByType<UnderwaterEnvironment>();
        school = FindAnyObjectByType<FishSchoolSystem>();
        if (school != null) school.EnableSnapshot();
        var terrain = FindAnyObjectByType<MeshGenerator>();
        areaCentre = terrain != null ? terrain.transform.position : Vector3.zero;

        wanderAngle = Random.Range(0f, 360f);
        targetHeight = transform.position.y;
        lastYaw = transform.eulerAngles.y;
        speed = cruiseSpeed;

        tree = BuildTree();
        tree.Start();
    }

    void OnDestroy()
    {
        if (tree != null && tree.CurrentState == Node.State.ACTIVE) tree.Stop();
    }

    // ------------------------------------------------------------------ behaviour tree

    Root BuildTree()
    {
        var blackboard = new Blackboard(UnityContext.GetClock());
        blackboard["danger"] = false;
        blackboard["chase"] = false;

        Node flee = new Sequence(
            Do(() =>
            {
                Begin(MoveMode.Flee, fleeSpeed, "Flee from shark");
                targetFn = () => sharkPosition;
            }),
            Until(() => sharkDistance > sharkSafeRadius, 8f),
            Do(() =>
            {
                Begin(MoveMode.Hover, 0f, "Look back");
                lookFn = () => sharkPosition;
            }),
            new Wait(2f),
            Do(() =>
            {
                calmUntil = Time.time + 6f;
                tree.Blackboard["danger"] = false;
            }));

        Node chase = new Sequence(
            Do(() =>
            {
                Begin(MoveMode.MoveTo, chaseSpeed, "Chase the treasure");
                targetFn = () => loot != null ? loot.position : transform.position;
            }),
            Until(() => loot == null || lootDistance < lootCatchDistance || lootDistance > lootRadius + 3f, 20f),
            new Selector(
                new Condition(() => lootDistance < lootCatchDistance + 1.5f, new Sequence(
                    Do(() =>
                    {
                        // A small victory lap: tight rising circle with a double roll.
                        Vector3 here = transform.position;
                        BeginOrbit(() => here, 1.6f, playSpeed, 0f, 0.8f, "Celebrate");
                        StartRoll(2f, 1.8f);
                    }),
                    new Wait(2.6f))),
                Do(() => { })),
            Do(() =>
            {
                ignoreLootUntil = Time.time + 15f;
                tree.Blackboard["chase"] = false;
            }));

        Node idle = new Sequence(
            Do(PickActivity),
            new Selector(
                When(Activity.Cruise, CruiseNode()),
                When(Activity.Rest, RestNode()),
                When(Activity.Play, PlayNode()),
                When(Activity.Surface, SurfaceNode()),
                When(Activity.SchoolDance, SchoolDanceNode()),
                When(Activity.VisitDiver, VisitDiverNode()),
                When(Activity.SeabedGlide, SeabedGlideNode())));

        return new Root(blackboard, new Service(0.2f, Sense, new Selector(
            new BlackboardCondition("danger", Operator.IS_EQUAL, true, Stops.LOWER_PRIORITY_IMMEDIATE_RESTART, flee),
            new BlackboardCondition("chase", Operator.IS_EQUAL, true, Stops.LOWER_PRIORITY_IMMEDIATE_RESTART, chase),
            idle)));
    }

    Node CruiseNode()
    {
        return new Sequence(
            Do(() => { Begin(MoveMode.Wander, cruiseSpeed, "Cruise"); targetHeight = RandomHeight(); }),
            new Wait(5f, 2f),
            Do(() => { targetHeight = RandomHeight(); }),
            new Wait(5f, 2f));
    }

    // Stops and looks around while treading water. The idle clip already holds her
    // nearly upright, so hovering needs no extra pitch (posePitch is added on top of it).
    Node RestNode()
    {
        return new Sequence(
            Do(() =>
            {
                Begin(MoveMode.Hover, 0f, "Rest");
                lookAround = true;
            }),
            new Wait(6f, 2f));
    }

    Node PlayNode()
    {
        return new Sequence(
            Do(() => { Begin(MoveMode.Wander, playSpeed, "Play: barrel roll"); targetHeight = RandomHeight(); }),
            new Wait(1.2f),
            Do(() => StartRoll(1f, 1.1f)),
            new Wait(2.2f),
            Do(() => StartRoll(-2f, 1.7f)),
            new Wait(2.6f),
            Do(() => { Begin(MoveMode.Wander, cruiseSpeed * 0.7f, "Play: somersault"); StartFlip(1.5f); }),
            new Wait(2.2f));
    }

    // Spirals up to just under the surface, looks up for a moment, then dives back down.
    Node SurfaceNode()
    {
        return new Sequence(
            Do(() =>
            {
                Vector3 here = transform.position + transform.right * 2.5f;
                BeginOrbit(() => here, 2.5f, cruiseSpeed * 1.2f, 0f, 1.2f, "Spiral up to the surface");
            }),
            Until(() => transform.position.y > Ceiling - 0.6f, 14f),
            Do(() => { Begin(MoveMode.Hover, 0f, "Look up at the surface"); posePitch = 15f; }),
            new Wait(3.5f, 1f),
            Do(() =>
            {
                Vector3 p = transform.position;
                float bottom = HasFloor ? floorY + floorClearance : p.y - 7f;
                Vector3 dive = p + transform.right * 7f;
                dive.y = Mathf.Max(bottom, p.y - 8f);
                Begin(MoveMode.MoveTo, playSpeed * 1.3f, "Dive");
                targetFn = () => dive;
            }),
            Until(() => DistanceToTarget() < 1.5f, 5f));
    }

    Node SchoolDanceNode()
    {
        return new Sequence(
            Do(() =>
            {
                Begin(MoveMode.MoveTo, cruiseSpeed * 1.3f, "Swim to a fish school");
                targetFn = () => schoolCentre;
            }),
            Until(() => DistanceToTarget() < 3.5f, 12f),
            Do(() => BeginOrbit(() => schoolCentre, 2.8f, cruiseSpeed * 1.25f, 0f, 0f, "Dance with the fish")),
            new Wait(4f, 1f),
            Do(() => StartRoll(1f, 1.3f)),
            new Wait(5f, 1.5f));
    }

    Node VisitDiverNode()
    {
        return new Sequence(
            Do(() =>
            {
                Begin(MoveMode.MoveTo, chaseSpeed, "Swim to the diver");
                targetFn = DiverPosition;
            }),
            Until(() => DistanceToTarget() < 4.5f, 15f),
            Do(() => BeginOrbit(DiverPosition, 3f, cruiseSpeed * 1.3f, 0.5f, 0f, "Circle the diver")),
            new Wait(7f, 1f),
            Do(() =>
            {
                Begin(MoveMode.Hover, 0f, "Greet the diver");
                lookFn = DiverPosition;
                StartRoll(1f, 1.2f);
            }),
            new Wait(2.5f));
    }

    Node SeabedGlideNode()
    {
        return new Sequence(
            Do(() => { Begin(MoveMode.Wander, cruiseSpeed * 1.2f, "Glide along the seabed"); hugFloor = true; }),
            new Wait(9f, 2f),
            Do(() => { hugFloor = false; targetHeight = RandomHeight(); StartRoll(1f, 1.2f); }),
            new Wait(1.5f));
    }

    Node Do(System.Action action) { return new Action(action); }

    Node When(Activity activity, Node node) { return new Condition(() => CurrentActivity == activity, node); }

    // Runs until the condition holds or the time is up; both count as success so that a
    // target she cannot reach only shortens the activity.
    Node Until(System.Func<bool> done, float timeout)
    {
        float started = 0f;
        return new Action((Action.Request request) =>
        {
            if (request == Action.Request.CANCEL) return Action.Result.FAILED;
            if (request == Action.Request.START) started = Time.time;
            return done() || Time.time - started > timeout ? Action.Result.SUCCESS : Action.Result.PROGRESS;
        });
    }

    void PickActivity()
    {
        Activity pick = Activity.Cruise;
        if (forced >= 0)
        {
            pick = (Activity)forced;
            forced = -1;
        }
        else
        {
            float total = 0f;
            int count = System.Enum.GetValues(typeof(Activity)).Length;
            for (int i = 0; i < count; i++) total += Weight((Activity)i);
            float r = Random.value * total;
            for (int i = 0; i < count; i++)
            {
                float w = Weight((Activity)i);
                if (w <= 0f) continue;
                pick = (Activity)i;
                if (r < w) break;
                r -= w;
            }
        }
        if (!Available(pick)) pick = Activity.Cruise;
        lastActivity = CurrentActivity = pick;
    }

    float Weight(Activity a)
    {
        if (a == lastActivity || !Available(a)) return 0f;
        int i = (int)a;
        return weights != null && i < weights.Length ? Mathf.Max(0f, weights[i]) : 1f;
    }

    bool Available(Activity a)
    {
        switch (a)
        {
            case Activity.SchoolDance:
                return school != null && school.FindSchool(transform.position, 25f, 4f, out schoolCentre);
            case Activity.VisitDiver:
                return diver != null && Vector3.Distance(diver.position, transform.position) < 40f;
            case Activity.SeabedGlide:
                return HasFloor;
            default:
                return true;
        }
    }

    // Makes the next idle activity the given one and starts it now. For testing and showcase shots.
    public void Force(Activity activity)
    {
        forced = (int)activity;
        if (tree == null) return;
        if (tree.CurrentState == Node.State.ACTIVE) tree.Stop();
        tree.Blackboard["danger"] = false;
        tree.Blackboard["chase"] = false;
        ignoreLootUntil = calmUntil = Time.time + 20f;
        tree.Start();
    }

    public string Report()
    {
        Vector3 v = rb.Velocity;
        return "state=" + State + " activity=" + CurrentActivity + " mode=" + mode
            + " pos=" + transform.position.ToString("F1") + " speed=" + v.magnitude.ToString("F2")
            + " pitch=" + pitch.ToString("F0") + " bank=" + bank.ToString("F0")
            + " floor=" + floorY.ToString("F1") + " ceiling=" + Ceiling.ToString("F1")
            + " loot=" + lootDistance.ToString("F1") + " shark=" + sharkDistance.ToString("F1")
            + " rolling=" + (rollTime < 1f) + " flipping=" + (flipTime < 1f);
    }

    void Sense()
    {
        Vector3 pos = transform.position;
        if (Time.time >= nextLookup)
        {
            nextLookup = Time.time + 1f;
            if (loot == null)
            {
                var found = GameObject.FindGameObjectWithTag("Respawn");
                if (found != null) loot = found.transform;
            }
            if (diver == null)
            {
                var found = FindAnyObjectByType<DiverMoveUnit>();
                if (found != null) diver = found.transform;
            }
        }

        lootDistance = loot != null ? Vector3.Distance(loot.position, pos) : float.MaxValue;
        if (lootDistance < lootRadius && Time.time > ignoreLootUntil) tree.Blackboard["chase"] = true;

        if (school != null && school.NearestPredator(pos, out sharkPosition, out sharkDistance))
        {
            if (sharkDistance < sharkAlarmRadius && Time.time > calmUntil) tree.Blackboard["danger"] = true;
        }
        else sharkDistance = float.MaxValue;

        if (school != null && CurrentActivity == Activity.SchoolDance)
        {
            Vector3 centre, heading;
            if (school.TrackSchool(schoolCentre, 5f, out centre, out heading)) schoolCentre = centre;
        }
    }

    // ------------------------------------------------------------------ motor

    void Begin(MoveMode newMode, float newSpeed, string state)
    {
        mode = newMode;
        speed = newSpeed;
        State = state;
        targetFn = null;
        lookFn = null;
        lookAround = false;
        hugFloor = false;
        posePitch = 0f;
        hasLastCentre = false;
        if (newMode == MoveMode.Wander)
        {
            Vector3 v = rb.Velocity;
            if (v.x * v.x + v.z * v.z > 0.05f) wanderAngle = Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg;
            else wanderAngle = Mathf.Atan2(transform.right.x, transform.right.z) * Mathf.Rad2Deg;
        }
    }

    void BeginOrbit(System.Func<Vector3> centre, float radius, float orbitSpeed, float height, float climb, string state)
    {
        Begin(MoveMode.Orbit, orbitSpeed, state);
        targetFn = centre;
        orbitRadius = radius;
        orbitHeight = height;
        orbitClimb = climb;
        orbitDir = Random.value < 0.5f ? -1f : 1f;
    }

    Vector3 DiverPosition() { return diver != null ? diver.position : transform.position; }

    float DistanceToTarget() { return targetFn != null ? Vector3.Distance(targetFn(), transform.position) : 0f; }

    float RandomHeight()
    {
        float lo = HasFloor ? floorY + floorClearance : transform.position.y - 2f;
        float hi = Ceiling - 1f;
        return hi > lo ? Random.Range(lo, hi) : hi;
    }

    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        Vector3 pos = transform.position;
        Vector3 vel = rb.Velocity;

        if (Time.time >= nextFloorCheck)
        {
            nextFloorCheck = Time.time + 0.15f;
            RaycastHit hit;
            floorY = Physics.Raycast(pos, Vector3.down, out hit, 60f, groundMask, QueryTriggerInteraction.Ignore)
                ? hit.point.y : float.NegativeInfinity;
        }

        Vector3 desired = Vector3.zero;
        float limit = speed;
        switch (mode)
        {
            case MoveMode.Wander:
            {
                wanderAngle += (Mathf.PerlinNoise(seed, Time.time * 0.3f) - 0.47f) * 110f * dt;
                Vector3 offset = pos - areaCentre;
                if (Mathf.Abs(offset.x) > areaHalfSize || Mathf.Abs(offset.z) > areaHalfSize)
                    wanderAngle = Mathf.MoveTowardsAngle(wanderAngle, Mathf.Atan2(-offset.x, -offset.z) * Mathf.Rad2Deg, 120f * dt);
                desired = Quaternion.Euler(0f, wanderAngle, 0f) * Vector3.forward * speed;
                float height = hugFloor && HasFloor ? floorY + 0.9f : targetHeight;
                desired.y = Mathf.Clamp((height - pos.y) * 0.8f, -speed * 0.6f, speed * 0.6f);
                break;
            }
            case MoveMode.MoveTo:
            {
                Vector3 to = targetFn() - pos;
                desired = to.normalized * speed * Mathf.Clamp01(to.magnitude / 1.5f);
                break;
            }
            case MoveMode.Orbit:
            {
                Vector3 centre = targetFn();
                Vector3 centreVelocity = hasLastCentre ? (centre - lastCentre) / dt : Vector3.zero;
                if (centreVelocity.sqrMagnitude > 64f) centreVelocity = Vector3.zero; // the centre jumped
                lastCentre = centre; hasLastCentre = true;
                orbitHeight += orbitClimb * dt;

                Vector3 radial = pos - centre; radial.y = 0f;
                float distance = radial.magnitude;
                radial = distance > 0.05f ? radial / distance : transform.right;
                Vector3 tangent = Vector3.Cross(Vector3.up, radial) * orbitDir;
                // Far away she heads for the circle; on it she follows the tangent.
                float onCircle = 1f - Mathf.Clamp01(Mathf.Abs(distance - orbitRadius) / (orbitRadius * 2f));
                desired = tangent * speed * onCircle + radial * Mathf.Clamp((orbitRadius - distance) * 1.5f, -speed, speed);
                desired.y = Mathf.Clamp((centre.y + orbitHeight - pos.y) * 1.2f, -speed * 0.7f, speed * 0.7f);
                desired += centreVelocity;
                limit = speed + centreVelocity.magnitude;
                break;
            }
            case MoveMode.Hover:
                limit = cruiseSpeed;
                break;
            case MoveMode.Flee:
            {
                Vector3 away = pos - targetFn();
                away.y *= 0.3f;
                Vector3 offset = pos - areaCentre; offset.y = 0f;
                if (offset.magnitude > areaHalfSize) away -= offset.normalized * away.magnitude; // do not run off the map
                if (away.sqrMagnitude < 0.01f) away = transform.right;
                desired = away.normalized * speed;
                break;
            }
        }

        // Stay between the seabed and the surface.
        float ceiling = Ceiling;
        if (pos.y > ceiling) desired.y = Mathf.Min(desired.y, (ceiling - pos.y) * 2f);
        float clearance = hugFloor ? 0.6f : floorClearance;
        if (HasFloor && pos.y < floorY + clearance) desired.y = Mathf.Max(desired.y, (floorY + clearance - pos.y) * 2f);

        float maxAccel = mode == MoveMode.Flee ? steering.maxAcceleration * 1.8f : steering.maxAcceleration;
        Vector3 accel = Vector3.ClampMagnitude((desired - vel) / 0.35f, maxAccel);

        if (mode != MoveMode.Hover && wallAvoidance != null)
        {
            Vector3 avoid = wallAvoidance.GetSteering();
            if (avoid.sqrMagnitude > 0.0001f)
            {
                accel = Vector3.ClampMagnitude(avoid, maxAccel * 1.5f);
                // Rocks here are pillars and ridges: going over them usually works.
                if (mode != MoveMode.Wander && pos.y < ceiling - 1f) accel.y += 5f;
                if (mode == MoveMode.Wander && vel.x * vel.x + vel.z * vel.z > 0.05f)
                    wanderAngle = Mathf.Atan2(vel.x, vel.z) * Mathf.Rad2Deg;
            }
        }

        steering.maxVelocity = Mathf.Max(limit * 1.1f, 0.5f);
        steering.Steer(accel);

        Vector3 after = rb.Velocity;
        if (after.x * after.x + after.z * after.z > 0.16f)
        {
            steering.LookWhereYoureGoing();
        }
        else if (lookFn != null || lookAround)
        {
            float yaw = rb.Rotation.eulerAngles.y;
            float wanted = yaw;
            if (lookFn != null)
            {
                Vector3 to = lookFn() - pos;
                if (to.x * to.x + to.z * to.z > 0.01f) wanted = -Mathf.Atan2(to.z, to.x) * Mathf.Rad2Deg;
            }
            else wanted = yaw + Mathf.Sin(Time.time * 0.7f + seed) * 50f;
            rb.Rotation = Quaternion.Euler(0f, Mathf.LerpAngle(yaw, wanted, dt * 1.5f), 0f);
        }
    }

    // ------------------------------------------------------------------ pose and animation

    // Positive turns roll to the right.
    void StartRoll(float turns, float duration)
    {
        rollTurns = turns; rollDuration = Mathf.Max(0.1f, duration); rollTime = 0f;
    }

    void StartFlip(float duration)
    {
        flipDuration = Mathf.Max(0.1f, duration); flipTime = 0f;
    }

    void LateUpdate()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        Vector3 v = rb.Velocity;
        float planar = new Vector2(v.x, v.z).magnitude;
        float sp = v.magnitude;

        float wantedPitch = posePitch;
        if (sp > 0.3f) wantedPitch += Mathf.Clamp(Mathf.Atan2(v.y, Mathf.Max(planar, 0.3f)) * Mathf.Rad2Deg, -maxPitch, maxPitch);
        pitch = Mathf.Lerp(pitch, wantedPitch, 1f - Mathf.Exp(-dt * 3.5f));

        float yaw = transform.eulerAngles.y;
        float yawRate = Mathf.DeltaAngle(lastYaw, yaw) / dt;
        lastYaw = yaw;
        float wantedBank = Mathf.Clamp(-yawRate * 0.4f, -maxBank, maxBank) * Mathf.Clamp01(sp / 1.5f);
        bank = Mathf.Lerp(bank, wantedBank, 1f - Mathf.Exp(-dt * 3f));

        float roll = 0f, flip = 0f;
        if (rollTime < 1f)
        {
            rollTime = Mathf.Min(1f, rollTime + dt / rollDuration);
            roll = -360f * rollTurns * Mathf.SmoothStep(0f, 1f, rollTime);
        }
        if (flipTime < 1f)
        {
            flipTime = Mathf.Min(1f, flipTime + dt / flipDuration);
            flip = 360f * Mathf.SmoothStep(0f, 1f, flipTime);
        }

        if (visual != null)
        {
            // Root faces +X: pitch turns about Z, roll about X.
            visual.localRotation = Quaternion.AngleAxis(pitch + flip, Vector3.forward)
                * Quaternion.AngleAxis(bank + roll, Vector3.right) * baseRotation;
            float bob = Mathf.Sin(Time.time * 1.3f + seed) * bobAmount * (1f - Mathf.Clamp01(sp / 1.5f));
            visual.localPosition = basePosition + Vector3.up * bob;
        }

        if (animator != null)
        {
            // "IsSeek" plays the swimming clip, otherwise the idle clip.
            bool swimming = sp > 0.5f;
            animator.SetBool("IsSeek", swimming);
            animator.speed = swimming ? Mathf.Lerp(0.8f, 2.4f, Mathf.InverseLerp(1f, fleeSpeed, sp)) : 1f;
        }
    }
}
