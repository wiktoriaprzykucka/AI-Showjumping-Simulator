using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Actuators;
using UnityEngine;

public class HorseAgent : Agent
{
    // ═══════════════════════════════════════════
    // TUNABLE PARAMETERS
    // ═══════════════════════════════════════════

    [Header("Movement")]
    public float moveSpeed = 10f;
    public float jumpForce = 4f;
    public float turnSpeed = 120f;

    [Tooltip("Fraction of moveSpeed above which the horse is considered 'galloping'.")]
    [Range(0.1f, 1f)] public float gallopSpeedFraction = 0.6f;

    [Header("Dense rewards (per physics step)")]
    [Tooltip("Reward per meter of progress toward the next hurdle (delta distance). " +
             "This is the agent's PRIMARY 'go toward the hurdle' signal — keep it strong, " +
             "or the horse will not learn to navigate.")]
    public float rewardProximityMult = 0.05f;

    [Tooltip("Small reward per step when horse.forward points at the next hurdle. Scaled by dot product (0..1).")]
    public float rewardFacingMult = 0.003f;

    [Tooltip("Small reward per step when horse.forward aligns with the hurdle's own forward direction (correct approach side). Scaled by how close you are.")]
    public float rewardApproachAlignMult = 0.004f;

    [Tooltip("Bonus per step when galloping AND facing the next hurdle. Speed with purpose.")]
    public float rewardGallopMult = 0.01f;

    [Tooltip("Legacy forward-speed reward. Kept small — progress reward already covers most of this.")]
    public float rewardSpeedMult = 0.003f;

    [Header("Event rewards")]
    public float rewardClearHurdle = 8f;
    [Tooltip("Extra bonus when the hurdle is cleared while the horse is actually jumping/airborne.")]
    public float rewardJumpThroughHurdleBonus = 8f;
    public float rewardLap = 20f;
    [Tooltip("One-time reward when the horse enters the finish gate trigger after clearing all hurdles. " +
             "Episode ends on this event (instead of ending mid-jump on the last hurdle).")]
    public float rewardFinishGate = 12f;

    [Header("Punishments (per step — KEEP SMALL or training stalls!)")]
    [Tooltip("Per-physics-step penalty when speed < stillThreshold. With 50fps physics, " +
             "-0.002 = -0.1/sec. KEEP THIS LOW — if it dwarfs the proximity reward, the " +
             "agent learns to never move (because trying-and-failing is worse than not trying).")]
    public float punishStandingStill = -0.002f;

    [Tooltip("Per-physics-step penalty when angular speed > spinningThreshold. Should be " +
             "SMALL because the horse must turn (often quickly) to navigate to hurdles. " +
             "Setting this too high makes the policy refuse to turn, which is the #1 cause " +
             "of 'horse just stands and twitches' failures.")]
    public float punishSpinning = -0.001f;

    [Header("Punishments (events)")]
    public float punishWrongJump = -0.3f;
    public float punishHitWall = -0.2f;
    public float punishTimerExpired = -1f;
    public float punishFallOff = -1f;

    [Header("Timing / Thresholds")]
    [Tooltip("Seconds allowed between hurdle clears. Reset every time a hurdle is cleared. " +
             "If it expires the horse is considered to have failed (it's stuck / wandering). " +
             "60s gives a learning policy enough slack to figure things out per hurdle.")]
    public float timePerHurdle = 60f;

    [Tooltip("Hard cap on total episode duration in seconds. Matches the real show-jumping " +
             "time-allowed of ~5 minutes per parkour. Episode ends when this expires " +
             "even if the horse is running — guarantees fresh starts during training.")]
    public float maxEpisodeTime = 300f;

    [Tooltip("Angular speed in deg/sec above which the horse is 'spinning'. MUST BE LARGER " +
             "than the agent's turnSpeed, or normal turning is punished and the agent will " +
             "refuse to rotate. Recommended: turnSpeed × 1.5 (so 180 if turnSpeed=120).")]
    public float spinningThreshold = 200f;

    [Tooltip("Horizontal speed in m/s below which the horse is 'standing still'. Lower = " +
             "more lenient (only punishes truly stopped). 0.3 is a good default.")]
    public float stillThreshold = 0.3f;

    [Tooltip("Meters. Jumping within this range of a hurdle is considered valid.")]
    public float nearHurdleRange = 6f;

    [Header("Setup")]
    public Transform groundCheck;
    public float groundRadius = 0.5f;
    public LayerMask groundLayer;
    public Transform[] hurdles;

    [Tooltip("Optional. Assign the finish line transform (the object that has the finish trigger). " +
             "CourseLoader auto-assigns this when your course JSON includes `finishSensor`. " +
             "If null, behavior matches the legacy flow: episode ends as soon as the last hurdle is cleared.")]
    public Transform finishGate;

    [Tooltip("Optional. Drag in your arena walls / corner markers (North, East, South, West). " +
             "If any are assigned, the agent observes wall clearance and is punished for leaving the arena. " +
             "Leave empty to disable arena awareness.")]
    public Transform[] boundaries;

    [Header("Spawn")]
    [Tooltip("If set, used to read `startPoint` from the loaded course. If null, falls back to " +
             "`courseLoader.coursePathManager` when available.")]
    public CoursePathManager coursePathManager;
    [Tooltip("When true and the path manager has a non-null startPoint (from JSON `startSensor`), " +
             "the horse is placed in world space at that transform (plus jitter / spawnLocalPosition). " +
             "When false or no start point, uses legacy local spawn under the agent's parent.")]
    public bool spawnAtStartSensorWhenAvailable = true;
    public Vector3 spawnLocalPosition = new Vector3(0f, 1f, 0f);
    [Range(0f, 5f)] public float spawnPositionJitter = 1.0f;
    [Range(0f, 180f)] public float spawnYawJitterDeg = 15f;

    [Header("Course (training)")]
    [Tooltip("Optional. If assigned AND `CourseLoader.swapCoursesDuringTraining` is enabled, the " +
             "course is rotated according to the loader's curriculum + pacing rules at the start of " +
             "each episode. Used during training to teach generalization. Leave empty for normal play.")]
    public CourseLoader courseLoader;

    // ═══════════════════════════════════════════
    // VISUAL OBSERVATIONS (Cameras)
    // ═══════════════════════════════════════════
    // Visual observations are added on top of the 18-float vector. They are auto-registered
    // by attaching `CameraSensorComponent`s in Initialize() (which runs before
    // ML-Agents' InitializeSensors() — see Agent.LazyInitialize in the package).
    //
    // Both camera slots are OPTIONAL. Drag the existing `HorsePOVCam` (child of the horse
    // prefab) and the scene's `TopDownCam` into the slots below to enable visual training.
    // The cameras' `enabled` state is NOT touched here — `CameraSwitcher` is free to toggle
    // them for human spectating, because `CameraSensor.Update()` calls `Camera.Render()`
    // directly each step regardless of the camera's enabled flag.
    //
    // The sensor NAMES below ("ForwardCam" and "TopCam") are the contract with CourseLoader:
    // CourseLoader.topCamSensorName defaults to "TopCam" and its stripCameraSensors tooltip
    // references both. Don't rename without updating the loader.

    [Header("Visual observations (cameras)")]
    [Tooltip("First-person camera mounted on the horse. If assigned, a CameraSensor is " +
             "created at runtime so the policy receives this view as a visual observation. " +
             "Leave empty to skip.")]
    public Camera horsePOVCamera;

    [Tooltip("Top-down arena camera. If assigned, a CameraSensor is created at runtime so " +
             "the policy also receives this bird's-eye view. Leave empty to skip.")]
    public Camera topDownCamera;

    [Tooltip("Width (pixels) of every visual observation. 84 is the ML-Agents default and a " +
             "good speed/quality tradeoff. Larger = more detail but much slower training.")]
    [Range(16, 256)] public int cameraSensorWidth = 84;

    [Tooltip("Height (pixels) of every visual observation. Match width unless you have a reason.")]
    [Range(16, 256)] public int cameraSensorHeight = 84;

    [Tooltip("If true, camera observations are converted to single-channel grayscale. " +
             "Halves the input size and usually trains faster on simple tasks. Color helps " +
             "if hurdle/arena colors carry meaning.")]
    public bool cameraSensorGrayscale = false;

    [Tooltip("Number of past camera frames stacked as a single observation. >1 lets the " +
             "policy see motion. 1 = no stacking.")]
    [Range(1, 6)] public int cameraSensorStacks = 1;

    [Tooltip("PNG compression saves bandwidth between Unity and the trainer; turn off only " +
             "for debugging — uncompressed visual streams are very heavy.")]
    public bool cameraSensorCompressPng = true;

    [Tooltip("If TRUE, the sensor cameras stay enabled at runtime so you can watch them in " +
             "the Game view. Costs one extra render per frame per camera (negligible at 84×84). " +
             "If FALSE, ML-Agents force-disables the cameras on Start (CameraSensorComponent." +
             "UpdateSensor sets Camera.enabled = RuntimeCameraEnable), so the sensors render " +
             "off-screen only and the Game view shows 'No cameras rendering' unless you have a " +
             "separate viewer camera that isn't a sensor target. Default TRUE because the " +
             "primary use case during development is watching training.")]
    public bool keepCameraSensorsVisible = true;

    // ═══════════════════════════════════════════
    // Internal state
    // ═══════════════════════════════════════════

    private Rigidbody rb;
    private Animator animator;
    private bool isGrounded;
    private bool hasJumped;
    private float jumpCooldown;
    private int currentHurdle;
    private float hurdleTimer;
    private float episodeTimer;
    private float prevDistToHurdle = float.MaxValue;
    private int lapsCompleted;

    /// <summary>True after the last hurdle is cleared while a <see cref="finishGate"/> is set — agent runs to finish.</summary>
    private bool awaitingFinish;
    /// <summary>Prevents double reward / double EndEpisode if the finish trigger is hit more than once.</summary>
    private bool finishConsumed;

    // Edge-detect for the out-of-arena penalty. Without this we'd charge `punishHitWall`
    // every physics step the horse spends outside (50 Hz × -0.2 = -10/sec), which dwarfs
    // every other reward signal and trains the policy to hug the inside walls.
    private bool wasOutsideArena;

    // Tracks the cumulative reward of THIS episode separately from ML-Agents' own counter
    // (which is reset synchronously inside EndEpisode → OnEpisodeBegin, so we can't read it
    // there). Used by CourseLoader's mastery-based curriculum to decide when to advance.
    private float thisEpisodeReward;

    // Wrapper around AddReward that ALSO tracks `thisEpisodeReward`. Replace every direct
    // AddReward(...) call with AwardReward(...) so the curriculum sees the same totals
    // ML-Agents does.
    private void AwardReward(float r)
    {
        AddReward(r);
        thisEpisodeReward += r;
    }

    /// <summary>The hurdle that must be cleared next (no modulo wrap — used for trigger matching only).</summary>
    private Transform CurrentHurdleTransform =>
        (hurdles != null && currentHurdle >= 0 && currentHurdle < hurdles.Length)
            ? hurdles[currentHurdle]
            : null;

    /// <summary>What the 18 vector observations and dense rewards aim at: current hurdle, or <see cref="finishGate"/> after the last clear.</summary>
    private Transform NavigationTarget {
        get {
            if (awaitingFinish && finishGate != null)
                return finishGate;
            return CurrentHurdleTransform;
        }
    }

    // Cached axis-aligned rectangle computed from the boundaries array
    private bool hasBounds;
    private float boundsMinX, boundsMaxX, boundsMinZ, boundsMaxZ;

    private void RecalculateBounds()
    {
        hasBounds = false;
        if (boundaries == null || boundaries.Length == 0) return;

        float xmin = float.MaxValue, xmax = float.MinValue;
        float zmin = float.MaxValue, zmax = float.MinValue;
        bool any = false;

        foreach (Transform b in boundaries)
        {
            if (b == null) continue;
            Vector3 p = b.position;
            xmin = Mathf.Min(xmin, p.x);
            xmax = Mathf.Max(xmax, p.x);
            zmin = Mathf.Min(zmin, p.z);
            zmax = Mathf.Max(zmax, p.z);
            any = true;
        }

        if (!any) return;

        boundsMinX = xmin;
        boundsMaxX = xmax;
        boundsMinZ = zmin;
        boundsMaxZ = zmax;
        hasBounds = true;
    }

    private float DistanceToNearestWall(Vector3 point)
    {
        if (!hasBounds) return float.MaxValue;

        float left   = Mathf.Abs(point.x - boundsMinX);
        float right  = Mathf.Abs(boundsMaxX - point.x);
        float bottom = Mathf.Abs(point.z - boundsMinZ);
        float top    = Mathf.Abs(boundsMaxZ - point.z);

        return Mathf.Min(left, right, bottom, top);
    }

    private bool IsInsideArena(Vector3 point)
    {
        if (!hasBounds) return true;

        return point.x >= boundsMinX && point.x <= boundsMaxX &&
               point.z >= boundsMinZ && point.z <= boundsMaxZ;
    }

    // ═══════════════════════════════════════════

    public override void Initialize()
    {
        rb = GetComponent<Rigidbody>();
        animator = GetComponent<Animator>();
        RecalculateBounds();
        SetupCameraSensors();
    }

    // Programmatically attach a CameraSensorComponent for each assigned camera. Done here
    // (in Initialize) because ML-Agents' Agent.LazyInitialize calls Initialize() BEFORE
    // InitializeSensors(), so any SensorComponent we add is automatically picked up by
    // GetComponents<SensorComponent>() during sensor registration.
    //
    // Idempotent: if a sensor with the same name already exists on this GameObject (e.g.
    // a leftover from a previous play session in the editor, or one the user authored
    // manually), we update its camera reference instead of creating a duplicate. This is
    // important because duplicate sensor names would change the observation layout the
    // trained model expects.
    private void SetupCameraSensors()
    {
        EnsureCameraSensor("ForwardCam", horsePOVCamera);
        EnsureCameraSensor("TopCam", topDownCamera);
    }

    private void EnsureCameraSensor(string sensorName, Camera cam)
    {
        if (cam == null) return;

        CameraSensorComponent existing = null;
        foreach (var c in GetComponents<CameraSensorComponent>())
        {
            if (c != null && c.SensorName == sensorName)
            {
                existing = c;
                break;
            }
        }

        var comp = existing != null ? existing : gameObject.AddComponent<CameraSensorComponent>();
        comp.SensorName = sensorName;
        comp.Camera = cam;
        comp.Width = cameraSensorWidth;
        comp.Height = cameraSensorHeight;
        comp.Grayscale = cameraSensorGrayscale;
        comp.ObservationStacks = cameraSensorStacks;
        comp.CompressionType = cameraSensorCompressPng
            ? SensorCompressionType.PNG
            : SensorCompressionType.None;
        // CameraSensorComponent.UpdateSensor() forces `Camera.enabled = RuntimeCameraEnable`
        // every time the sensor reconfigures (and once on Start). The default is FALSE, which
        // disables every sensor camera at runtime and is exactly what causes "Display 1 - No
        // cameras rendering". We surface this as a user-facing toggle and default it to TRUE
        // so the cameras stay visible. The sensor still renders to its own RenderTexture each
        // step via a manual Camera.Render() (see CameraSensor.ObservationToTexture), so
        // observation collection works with either setting.
        comp.RuntimeCameraEnable = keepCameraSensorsVisible;
    }

    public override void OnEpisodeBegin()
    {
        // Feed the just-finished episode's total reward to CourseLoader so its mastery-based
        // curriculum can decide whether the agent has 'mastered' the current course. This must
        // happen BEFORE LoadNextTrainingCourse so the loader sees the latest reward sample.
        // Rotate / rebuild the course (re-wires `hurdles`) BEFORE the spawn / state reset so
        // CollectObservations on this same episode sees the right layout.
        if (courseLoader != null && courseLoader.swapCoursesDuringTraining)
        {
            courseLoader.RecordEpisodeReward(thisEpisodeReward);
            courseLoader.LoadNextTrainingCourse();
            RecalculateBounds();
        }
        thisEpisodeReward = 0f;

        RepositionAtCourseStart(applyJitter: true);

        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        hasJumped = false;
        jumpCooldown = 0f;
        currentHurdle = 0;
        hurdleTimer = timePerHurdle;
        episodeTimer = maxEpisodeTime;
        lapsCompleted = 0;
        awaitingFinish = false;
        finishConsumed = false;
        prevDistToHurdle = float.MaxValue;
        wasOutsideArena = false;
    }

    /// <summary>
    /// Places the agent at the course start marker from JSON when available.
    /// Called each episode with jitter (training); <see cref="CourseLoader"/> calls with <paramref name="applyJitter"/> false after loading so the horse appears on the sensor even if the course was chosen after Play.
    /// </summary>
    /// <param name="overrideStart">When non-null (e.g. from <see cref="CourseLoader"/> right after load), used even if <see cref="coursePathManager"/> / <see cref="courseLoader"/> are not wired on this agent.</param>
    public void RepositionAtCourseStart(bool applyJitter, Transform overrideStart = null)
    {
        Transform startPt;
        if (!spawnAtStartSensorWhenAvailable)
            startPt = null;
        else
            startPt = overrideStart != null ? overrideStart : ResolveCourseStartPointTransform();

        Vector3 jitter = applyJitter
            ? new Vector3(
                Random.Range(-spawnPositionJitter, spawnPositionJitter),
                0f,
                Random.Range(-spawnPositionJitter, spawnPositionJitter))
            : Vector3.zero;
        float yawExtra = applyJitter ? Random.Range(-spawnYawJitterDeg, spawnYawJitterDeg) : 0f;

        if (startPt != null)
        {
            Vector3 localOffset = spawnLocalPosition + jitter;
            Vector3 worldPos = startPt.position + startPt.rotation * localOffset;
            float yaw = startPt.eulerAngles.y + yawExtra;
            transform.SetPositionAndRotation(worldPos, Quaternion.Euler(0f, yaw, 0f));
        }
        else
        {
            transform.localPosition = spawnLocalPosition + jitter;
            transform.localRotation = Quaternion.Euler(
                0f,
                applyJitter ? Random.Range(-spawnYawJitterDeg, spawnYawJitterDeg) : 0f,
                0f);
        }
    }

    Transform ResolveCourseStartPointTransform()
    {
        if (!spawnAtStartSensorWhenAvailable)
            return null;

        CoursePathManager pathMgr = coursePathManager != null
            ? coursePathManager
            : (courseLoader != null ? courseLoader.coursePathManager : null);
        Transform startPt = pathMgr != null ? pathMgr.startPoint : null;
        if (startPt == null && courseLoader != null)
            startPt = courseLoader.LoadedStartSensor;
        return startPt;
    }

    // ─────────────────────────────────────────────
    // OBSERVATIONS  (18 total — set VectorObservationSize = 18 in the Behavior Parameters)
    // ─────────────────────────────────────────────
    public override void CollectObservations(VectorSensor sensor)
    {
        Transform target = NavigationTarget;

        if (target == null || rb == null)
        {
            for (int i = 0; i < 18; i++) sensor.AddObservation(0f);
            return;
        }

        // Local linear velocity — 3
        Vector3 localVel = transform.InverseTransformDirection(rb.linearVelocity) / Mathf.Max(0.01f, moveSpeed);
        sensor.AddObservation(localVel);

        // Grounded — 1
        sensor.AddObservation(isGrounded ? 1f : 0f);

        // Angular velocity Y (deg/sec, normalized) — 1
        float angY_degPerSec = rb.angularVelocity.y * Mathf.Rad2Deg;
        sensor.AddObservation(angY_degPerSec / Mathf.Max(1f, spinningThreshold));

        // Direction to hurdle in horse local space — 3
        Vector3 toHurdle = target.position - transform.position;
        float dist = toHurdle.magnitude;
        Vector3 localToHurdle = transform.InverseTransformDirection(
            dist > 0.001f ? toHurdle / dist : transform.forward);
        sensor.AddObservation(localToHurdle);

        // Normalized distance — 1
        sensor.AddObservation(Mathf.Clamp01(dist / 50f));

        // Hurdle's own forward direction in horse local space — 3
        Vector3 localHurdleFwd = transform.InverseTransformDirection(target.forward);
        sensor.AddObservation(localHurdleFwd);

        // Scalar: how aligned horse.forward is with hurdle.forward (correct approach direction) — 1
        float approachAlign = Vector3.Dot(transform.forward, target.forward);
        sensor.AddObservation(approachAlign);

        // Scalar: how directly horse is facing the hurdle — 1
        float facing = dist > 0.001f
            ? Vector3.Dot(transform.forward, toHurdle / dist)
            : 1f;
        sensor.AddObservation(facing);

        // Hurdle timer normalized — 1
        sensor.AddObservation(hurdleTimer / Mathf.Max(0.01f, timePerHurdle));

        // Near-hurdle flag — 1
        sensor.AddObservation(dist < nearHurdleRange ? 1f : 0f);

        // Jump cooldown active — 1
        sensor.AddObservation(jumpCooldown > 0f ? 1f : 0f);

        // Wall clearance — 1
        float wallClear = 1f;
        if (hasBounds)
            wallClear = Mathf.Clamp01(DistanceToNearestWall(transform.position) / 10f);
        sensor.AddObservation(wallClear);

        // TOTAL: 3+1+1+3+1+3+1+1+1+1+1+1 = 18
    }

    // ─────────────────────────────────────────────
    // ACTIONS + REWARDS
    // ─────────────────────────────────────────────
    public override void OnActionReceived(ActionBuffers actions)
    {
        float move = Mathf.Clamp(actions.ContinuousActions[0], -0.3f, 1f);
        float turn = Mathf.Clamp(actions.ContinuousActions[1], -1f, 1f);
        float jump = actions.ContinuousActions[2];

        // ── Movement ──
        Vector3 delta = transform.forward * move * moveSpeed * Time.fixedDeltaTime;
        if (rb != null) rb.MovePosition(rb.position + delta);
        transform.Rotate(Vector3.up * turn * turnSpeed * Time.fixedDeltaTime);

        // ── Ground check ──
        if (groundCheck != null)
            isGrounded = Physics.CheckSphere(groundCheck.position, groundRadius, groundLayer);

        jumpCooldown -= Time.fixedDeltaTime;
        if (isGrounded) hasJumped = false;

        Transform target = NavigationTarget;
        if (target == null || rb == null) return;

        float distNow = Vector3.Distance(transform.position, target.position);
        bool nearHurdle = distNow < nearHurdleRange;

        // ── Jump ──
        if (jump > 0.5f && isGrounded && !hasJumped && jumpCooldown <= 0f)
        {
            rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
            rb.AddForce(Vector3.up * jumpForce, ForceMode.Impulse);
            hasJumped = true;
            jumpCooldown = 1.5f;

            if (animator != null) animator.SetTrigger("Jump");

            if (!nearHurdle) AwardReward(punishWrongJump);
        }

        // ── Animator ──
        if (animator != null)
            animator.SetFloat("Speed", Mathf.Abs(move) * moveSpeed);

        // ── Hurdle timer (failure: no progress between hurdles) ──
        hurdleTimer -= Time.fixedDeltaTime;
        if (hurdleTimer <= 0f)
        {
            AwardReward(punishTimerExpired);
            EndEpisode();
            return;
        }

        // ── Episode timer (hard cap: reset even on a perfect run) ──
        episodeTimer -= Time.fixedDeltaTime;
        if (episodeTimer <= 0f)
        {
            EndEpisode();
            return;
        }

        // ───────── REWARDS ─────────

        // Flat direction to hurdle (ignore vertical difference for heading)
        Vector3 flatToHurdle = target.position - transform.position;
        flatToHurdle.y = 0f;
        Vector3 toHurdleDir = flatToHurdle.sqrMagnitude > 0.0001f
            ? flatToHurdle.normalized
            : transform.forward;

        float forwardSpeed = Vector3.Dot(rb.linearVelocity, transform.forward);

        // 1. Progress (dense, main signal)
        if (prevDistToHurdle < float.MaxValue)
        {
            float progress = prevDistToHurdle - distNow;
            AwardReward(progress * rewardProximityMult);
        }
        prevDistToHurdle = distNow;

        // 2. Small legacy forward-speed reward
        if (forwardSpeed > 0f)
            AwardReward(forwardSpeed * rewardSpeedMult);

        // 3. Facing the next hurdle
        float facing = Vector3.Dot(transform.forward, toHurdleDir);
        if (facing > 0f)
            AwardReward(facing * rewardFacingMult);

        // 4. Approach alignment — horse.forward matching hurdle.forward.
        //    Weighted higher when close (so the agent learns to curve in, not just charge).
        float approachAlign = Vector3.Dot(transform.forward, target.forward);
        if (approachAlign > 0f)
        {
            float closeness = Mathf.Clamp01(1f - distNow / 20f);
            AwardReward(approachAlign * closeness * rewardApproachAlignMult);
        }

        // 5. Gallop bonus — big but gated on BOTH speed and correct heading
        bool isGalloping = forwardSpeed > gallopSpeedFraction * moveSpeed;
        if (isGalloping && facing > 0.7f)
            AwardReward(rewardGallopMult);

        // ───────── PUNISHMENTS ─────────

        // Standing still — use flat magnitude so reversing still counts as moving
        Vector3 flatVel = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
        if (flatVel.magnitude < stillThreshold)
            AwardReward(punishStandingStill);

        // Spinning — combine the COMMANDED turn rate with the physics-driven angular
        // velocity. The agent rotates via transform.Rotate (which doesn't update
        // rb.angularVelocity), so a check on the rigidbody alone would never see the
        // policy's own turning and the punishment would only ever fire on collision-
        // induced spins. Convert both to deg/sec to match `spinningThreshold`'s units.
        float commandedAngSpeedDegPerSec = Mathf.Abs(turn) * turnSpeed;
        float physicsAngSpeedDegPerSec = Mathf.Abs(rb.angularVelocity.y) * Mathf.Rad2Deg;
        float angSpeedDegPerSec = Mathf.Max(commandedAngSpeedDegPerSec, physicsAngSpeedDegPerSec);
        if (angSpeedDegPerSec > spinningThreshold)
            AwardReward(punishSpinning);

        // Fell off world
        if (transform.position.y < -5f)
        {
            AwardReward(punishFallOff);
            EndEpisode();
            return;
        }

        // Left arena (if bounds assigned). Charge the penalty ONCE on the rising edge —
        // re-entering and leaving again costs another hit, but standing outside doesn't
        // bleed reward at 50 Hz.
        bool outsideArena = hasBounds && !IsInsideArena(transform.position);
        if (outsideArena && !wasOutsideArena)
            AwardReward(punishHitWall);
        wasOutsideArena = outsideArena;
    }

    // ─────────────────────────────────────────────
    // TRIGGERS / COLLISIONS
    // ─────────────────────────────────────────────
    private void OnTriggerEnter(Collider other)
    {
        if (awaitingFinish && !finishConsumed && finishGate != null &&
            ColliderBelongsToFinishGate(other, finishGate))
        {
            finishConsumed = true;
            AwardReward(rewardFinishGate);
            EndEpisode();
            return;
        }

        Transform target = CurrentHurdleTransform;
        if (target == null) return;

        Transform t = other.transform;
        bool isTarget = t == target || t.IsChildOf(target);
        if (!isTarget) return;

        AwardReward(rewardClearHurdle);
        if (hasJumped || !isGrounded)
            AwardReward(rewardJumpThroughHurdleBonus);

        currentHurdle++;
        hurdleTimer = timePerHurdle;
        prevDistToHurdle = float.MaxValue;

        if (hurdles != null && currentHurdle >= hurdles.Length)
        {
            lapsCompleted++;
            AwardReward(rewardLap);
            if (finishGate != null)
                awaitingFinish = true;
            else
                EndEpisode();
        }
    }

    /// <summary>True if this collider is on <see cref="finishGate"/> or one of its children (finish trigger volumes).</summary>
    static bool ColliderBelongsToFinishGate(Collider other, Transform finishGate)
    {
        if (finishGate == null || other == null) return false;
        Transform tr = other.transform;
        return tr == finishGate || tr.IsChildOf(finishGate);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!collision.gameObject.CompareTag("Ground"))
            AwardReward(punishHitWall);
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var a = actionsOut.ContinuousActions;
        a[0] = Input.GetAxis("Vertical");
        a[1] = Input.GetAxis("Horizontal");
        a[2] = Input.GetKey(KeyCode.Space) ? 1f : 0f;
    }

    private void OnDrawGizmosSelected()
    {
        if (groundCheck != null)
        {
            Gizmos.color = isGrounded ? Color.green : Color.red;
            Gizmos.DrawWireSphere(groundCheck.position, groundRadius);
        }

        // Visualize boundaries rectangle in the editor
        RecalculateBounds();
        if (hasBounds)
        {
            Gizmos.color = Color.magenta;
            Vector3 c = new Vector3(
                (boundsMinX + boundsMaxX) * 0.5f,
                transform.position.y,
                (boundsMinZ + boundsMaxZ) * 0.5f);
            Vector3 s = new Vector3(
                boundsMaxX - boundsMinX,
                0.1f,
                boundsMaxZ - boundsMinZ);
            Gizmos.DrawWireCube(c, s);
        }

        Transform t = NavigationTarget;
        if (t == null) return;

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(t.position, nearHurdleRange);

        // Show the hurdle's "correct approach" axis
        Gizmos.color = Color.cyan;
        Gizmos.DrawRay(t.position, t.forward * 3f);
    }
}
