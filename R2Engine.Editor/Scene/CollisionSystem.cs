using System.Numerics;
using System.Runtime.CompilerServices;

namespace R2Engine.Editor.Scene;

public sealed class CollisionSystem
{
    public float CameraSweep(Scene scene, GameObject target, GameObject camera,
        Vector3 origin, Vector3 destination, float radius, uint collisionMask)
    {
        Vector3 delta = destination - origin;
        float limit = delta.Length();
        if (limit < 0.00001f) return 0;
        Vector3 direction = delta / limit;
        bool Excluded(GameObject obj)
        {
            for (GameObject? ancestor = obj; ancestor != null; ancestor = ancestor.Parent)
                if (ancestor == target || ancestor == camera) return true;
            return false;
        }
        // Distance fields are 1-Lipschitz: advancing by the remaining clearance
        // cannot jump through even a thin surface. Stop conservatively at contact.
        void Sweep(Func<Vector3, float> distance, Vector3 separation)
        {
            // Ignore only an initial radius overlap that separates from this
            // convex feature. Other colliders/triangles are still swept normally.
            float initial=distance(origin);
            if(initial>=0 && initial<=radius+0.001f && Vector3.Dot(separation,direction)>0.0000001f &&
                distance(origin+direction*limit)>radius+0.001f) return;
            float travel = 0;
            for (int iteration = 0; iteration < 48 && travel < limit; iteration++)
            {
                float gap = distance(origin + direction * travel) - radius;
                if (gap <= 0.001f) { limit = travel; return; }
                travel += gap;
            }
            if (travel < limit) limit = travel;
        }
        foreach (GameObject obj in scene.GameObjects)
        {
            if (!obj.IsActiveInHierarchy || Excluded(obj)) continue;
            foreach (Collider collider in obj.Components.OfType<Collider>())
            {
                if (collider.IsTrigger) continue;
                if ((collisionMask & (1u << Math.Clamp(collider.Layer, 0, 31))) == 0u) continue;
                if (collider is MeshCollider mesh)
                {
                    Vector3 low = Vector3.Min(origin, destination) - new Vector3(radius);
                    Vector3 high = Vector3.Max(origin, destination) + new Vector3(radius);
                    foreach (var (a, b, c) in MeshTriangles(mesh))
                    {
                        Vector3 min = Vector3.Min(a, Vector3.Min(b, c)), max = Vector3.Max(a, Vector3.Max(b, c));
                        if (max.X < low.X || min.X > high.X || max.Y < low.Y || min.Y > high.Y || max.Z < low.Z || min.Z > high.Z) continue;
                        if (Vector3.Cross(b-a, c-a).LengthSquared() < 1e-12f) continue;
                        Sweep(point => Vector3.Distance(point, ClosestPointOnTriangle(point, a, b, c)),
                            origin-ClosestPointOnTriangle(origin,a,b,c));
                    }
                }
                else if (collider is BoxCollider box)
                {
                    Vector3 rotation = obj.Transform.WorldRotation * (MathF.PI / 180);
                    Quaternion orientation = Quaternion.CreateFromYawPitchRoll(rotation.Y, rotation.X, rotation.Z);
                    Quaternion inverse = Quaternion.Inverse(orientation);
                    Vector3 center = obj.Transform.WorldPosition + Vector3.Transform(box.Center * obj.Transform.WorldScale, orientation);
                    Vector3 half = Vector3.Abs(box.Size * obj.Transform.WorldScale) * 0.5f;
                    Vector3 localOrigin=Vector3.Transform(origin-center,inverse);
                    Sweep(point => { Vector3 local = Vector3.Transform(point-center, inverse); return Vector3.Distance(local, Vector3.Clamp(local, -half, half)); },
                        Vector3.Transform(localOrigin-Vector3.Clamp(localOrigin,-half,half),orientation));
                }
                else if (collider is SphereCollider sphere)
                    Sweep(point => Vector3.Distance(point, Center(sphere)) - SphereRadius(sphere),origin-Center(sphere));
                else if (collider is CapsuleCollider capsule)
                {
                    var segment=CapsuleSegment(capsule);
                    Vector3 ab=segment.B-segment.A;
                    float t=ab.LengthSquared()>0 ? Math.Clamp(Vector3.Dot(origin-segment.A,ab)/ab.LengthSquared(),0,1) : 0;
                    Sweep(point => PointSegmentDistance(point, segment) - CapsuleRadius(capsule),origin-(segment.A+ab*t));
                }
            }
        }
        return limit;
    }

    private readonly HashSet<ColliderPair> _activePairs = new();
    private bool _reportedColliderCount;

    public void Reset()
    {
        _activePairs.Clear();
        _reportedColliderCount = false;
    }

    public void Update(Scene scene, Action<string>? logger = null)
    {
        Collider[] colliders = scene.GameObjects
            .Where(gameObject => gameObject.IsActiveInHierarchy)
            .SelectMany(gameObject => gameObject.Components)
            .OfType<Collider>()
            .ToArray();

        if (!_reportedColliderCount)
        {
            logger?.Invoke($"Collision system: {colliders.Length} collider(s) active.");

            foreach (Collider collider in colliders)
            {
                Vector3 center = Center(collider);
                logger?.Invoke(Describe(collider, center));
            }

            _reportedColliderCount = true;
        }

        HashSet<ColliderPair> currentPairs = new();

        for (int first = 0; first < colliders.Length; first++)
        {
            for (int second = first + 1; second < colliders.Length; second++)
            {
                Collider a = colliders[first];
                Collider b = colliders[second];

                if (!CanInteract(a, b))
                    continue;

                ColliderPair pair = new(a, b);
                bool wasActive = _activePairs.Contains(pair);
                if (!Overlaps(a, b) && (!wasActive || !RemainsInContact(a, b)))
                    continue;

                currentPairs.Add(pair);

                if (!wasActive)
                {
                    logger?.Invoke(
                        $"{(pair.A.IsTrigger || pair.B.IsTrigger ? "Trigger" : "Collision")} entered: " +
                        $"{pair.A.GameObject.Name} <-> {pair.B.GameObject.Name}");
                    Notify(pair, entering: true);
                }
                else
                {
                    NotifyStay(pair);
                }
            }
        }

        foreach (ColliderPair pair in _activePairs)
        {
            if (!currentPairs.Contains(pair))
            {
                logger?.Invoke(
                    $"{(pair.A.IsTrigger || pair.B.IsTrigger ? "Trigger" : "Collision")} exited: " +
                    $"{pair.A.GameObject.Name} <-> {pair.B.GameObject.Name}");
                Notify(pair, entering: false);
            }
        }

        _activePairs.Clear();
        _activePairs.UnionWith(currentPairs);
    }

    public Vector3 Move(GameObject gameObject, Vector3 displacement, Scene scene)
    {
        Vector3 start = gameObject.Transform.WorldPosition;
        MoveAlongAxis(gameObject, new Vector3(displacement.X, 0.0f, 0.0f), scene);
        MoveAlongAxis(gameObject, new Vector3(0.0f, displacement.Y, 0.0f), scene);
        MoveAlongAxis(gameObject, new Vector3(0.0f, 0.0f, displacement.Z), scene);
        return gameObject.Transform.WorldPosition - start;
    }

    public Vector3 MoveCharacter(
        GameObject gameObject,
        Vector3 displacement,
        Scene scene,
        bool wasGrounded,
        float stepHeight,
        float maxSlopeAngle,
        float groundSnapDistance)
    {
        Vector3 start = gameObject.Transform.WorldPosition;
        Vector3 moved = Move(gameObject, displacement, scene);
        float requestedHorizontal = new Vector2(displacement.X, displacement.Z).Length();
        float movedHorizontal = new Vector2(moved.X, moved.Z).Length();
        const float tolerance = 0.0001f;

        if (wasGrounded && requestedHorizontal > tolerance && movedHorizontal + tolerance < requestedHorizontal)
        {
            gameObject.Transform.SetWorldPosition(start);
            float maximumStep = Math.Max(0.0f, stepHeight);
            const int attempts = 8;
            for (int attempt = 1; attempt <= attempts; attempt++)
            {
                float lift = maximumStep * attempt / attempts;
                gameObject.Transform.SetWorldPosition(start);
                Vector3 raised = Move(gameObject, Vector3.UnitY * lift, scene);
                if (raised.Y + tolerance < lift) continue;
                Vector3 stepped = Move(gameObject, displacement, scene);
                float steppedHorizontal = new Vector2(stepped.X, stepped.Z).Length();
                if (steppedHorizontal + tolerance < requestedHorizontal) continue;
                Move(gameObject, -Vector3.UnitY * (lift + Math.Max(0.0f, groundSnapDistance)), scene);
                if (IsWalkableGround(gameObject, scene, maxSlopeAngle, groundSnapDistance + 0.08f))
                    return gameObject.Transform.WorldPosition - start;
            }
            gameObject.Transform.SetWorldPosition(start);
            moved = Move(gameObject, displacement, scene);
        }

        if (wasGrounded && groundSnapDistance > 0.0f)
        {
            Vector3 beforeSnap = gameObject.Transform.WorldPosition;
            Move(gameObject, -Vector3.UnitY * groundSnapDistance, scene);
            if (!IsWalkableGround(gameObject, scene, maxSlopeAngle, groundSnapDistance + 0.08f))
                gameObject.Transform.SetWorldPosition(beforeSnap);
        }

        return gameObject.Transform.WorldPosition - start;
    }

    private static bool IsWalkableGround(GameObject gameObject, Scene scene, float maxSlopeAngle, float range)
    {
        if (!TryGetGroundNormal(gameObject, scene, range, out Vector3 normal)) return false;
        float angle = MathF.Acos(Math.Clamp(Vector3.Dot(normal, Vector3.UnitY), -1.0f, 1.0f)) * 180.0f / MathF.PI;
        return angle <= Math.Clamp(maxSlopeAngle, 0.0f, 89.9f);
    }

    private static bool TryGetGroundNormal(GameObject gameObject, Scene scene, float range, out Vector3 normal)
    {
        normal = Vector3.UnitY;
        Collider? moving = gameObject.Components.OfType<Collider>().FirstOrDefault(item => !item.IsTrigger);
        if (moving == null) return false;
        Vector3 probe = moving switch
        {
            CapsuleCollider capsule => CapsuleSegment(capsule).A - Vector3.UnitY * CapsuleRadius(capsule),
            SphereCollider sphere => Center(sphere) - Vector3.UnitY * SphereRadius(sphere),
            BoxCollider box => Center(box) - Vector3.UnitY * BoxHalfSize(box).Y,
            _ => Center(moving)
        };
        float closestDistance = Math.Max(0.01f, range);
        bool found = false;
        foreach (Collider obstacle in scene.GameObjects
                     .Where(item => item.IsActiveInHierarchy && !ReferenceEquals(item, gameObject))
                     .SelectMany(item => item.Components).OfType<Collider>()
                     .Where(item => !item.IsTrigger && CanInteract(moving, item)))
        {
            if (obstacle is MeshCollider mesh)
            {
                foreach ((Vector3 a, Vector3 b, Vector3 c) in MeshTriangles(mesh))
                {
                    Vector3 point = ClosestPointOnTriangle(probe, a, b, c);
                    float distance = Vector3.Distance(probe, point);
                    if (distance > closestDistance || point.Y > probe.Y + 0.08f) continue;
                    Vector3 candidate = Vector3.Cross(b - a, c - a);
                    if (candidate.LengthSquared() <= 0.000001f) continue;
                    candidate = Vector3.Normalize(candidate);
                    if (candidate.Y < 0.0f) candidate = -candidate;
                    normal = candidate;
                    closestDistance = distance;
                    found = true;
                }
            }
            else if (obstacle is BoxCollider box)
            {
                Vector3 center = Center(box), half = BoxHalfSize(box);
                float top = center.Y + half.Y;
                if (probe.X >= center.X - half.X && probe.X <= center.X + half.X &&
                    probe.Z >= center.Z - half.Z && probe.Z <= center.Z + half.Z &&
                    MathF.Abs(probe.Y - top) <= closestDistance)
                {
                    normal = Vector3.UnitY;
                    closestDistance = MathF.Abs(probe.Y - top);
                    found = true;
                }
            }
        }
        return found;
    }

    public void ResolveBlockedVelocity(Rigidbody body, Vector3 axis, Scene scene)
    {
        Collider[] ownColliders = body.GameObject.Components.OfType<Collider>()
            .Where(collider => !collider.IsTrigger).ToArray();
        Collider? hit = scene.GameObjects
            .Where(other => other.IsActiveInHierarchy && !ReferenceEquals(other, body.GameObject))
            .SelectMany(other => other.Components).OfType<Collider>()
            .Where(collider => !collider.IsTrigger)
            .FirstOrDefault(other => ownColliders.Any(own => CanInteract(own, other) && Overlaps(own, other)));

        if (hit == null)
        {
            body.Velocity -= axis * Vector3.Dot(body.Velocity, axis);
            return;
        }

        Rigidbody? otherBody = hit.GameObject.GetComponent<Rigidbody>();
        float inverseMassA = body.BodyType == RigidbodyBodyType.Dynamic
            ? 1.0f / Math.Max(0.0001f, body.Mass)
            : 0.0f;
        float inverseMassB = otherBody?.BodyType == RigidbodyBodyType.Dynamic
            ? 1.0f / Math.Max(0.0001f, otherBody.Mass)
            : 0.0f;
        float inverseMassSum = inverseMassA + inverseMassB;
        if (inverseMassSum <= 0.0f)
        {
            body.Velocity -= axis * Vector3.Dot(body.Velocity, axis);
            return;
        }

        Vector3 normal = axis.LengthSquared() <= 0.000001f ? Vector3.UnitY : Vector3.Normalize(axis);
        Vector3 otherVelocity = otherBody?.Velocity ?? Vector3.Zero;
        Vector3 relativeVelocity = body.Velocity - otherVelocity;
        float closingSpeed = Vector3.Dot(relativeVelocity, normal);
        if (closingSpeed <= 0.0f)
            return;

        float restitution = Math.Clamp(MathF.Max(body.Restitution, otherBody?.Restitution ?? 0.0f), 0.0f, 1.0f);
        float impulseMagnitude = (1.0f + restitution) * closingSpeed / inverseMassSum;
        Vector3 impulse = impulseMagnitude * normal;
        body.Velocity -= impulse * inverseMassA;
        if (otherBody != null && inverseMassB > 0.0f)
            otherBody.Velocity += impulse * inverseMassB;

        Vector3 postRelativeVelocity = body.Velocity - (otherBody?.Velocity ?? Vector3.Zero);
        Vector3 tangent = postRelativeVelocity - normal * Vector3.Dot(postRelativeVelocity, normal);
        float tangentLength = tangent.Length();
        if (tangentLength <= 0.00001f) return;
        tangent /= tangentLength;
        float frictionImpulse = -Vector3.Dot(postRelativeVelocity, tangent) / inverseMassSum;
        float friction = MathF.Sqrt(Math.Clamp(body.Friction, 0.0f, 1.0f) *
                                    Math.Clamp(otherBody?.Friction ?? 0.5f, 0.0f, 1.0f));
        frictionImpulse = Math.Clamp(frictionImpulse, -impulseMagnitude * friction, impulseMagnitude * friction);
        Vector3 tangentImpulse = frictionImpulse * tangent;
        body.Velocity += tangentImpulse * inverseMassA;
        if (otherBody != null && inverseMassB > 0.0f)
            otherBody.Velocity -= tangentImpulse * inverseMassB;
    }

    private static void MoveAlongAxis(GameObject gameObject, Vector3 displacement, Scene scene)
    {
        float distance = displacement.Length();
        if (distance <= 0.0000001f) return;
        float smallestColliderRadius = gameObject.Components.OfType<Collider>()
            .Where(collider => !collider.IsTrigger)
            .Select(collider => collider switch
            {
                BoxCollider box => MathF.Min(BoxHalfSize(box).X, MathF.Min(BoxHalfSize(box).Y, BoxHalfSize(box).Z)),
                SphereCollider sphere => SphereRadius(sphere),
                CapsuleCollider capsule => CapsuleRadius(capsule),
                _ => 0.16f
            })
            .DefaultIfEmpty(0.16f)
            .Min();
        float maximumSubstep = Math.Clamp(smallestColliderRadius * 0.5f, 0.01f, 0.08f);
        int steps = Math.Max(1, (int)MathF.Ceiling(distance / maximumSubstep));
        Vector3 step = displacement / steps;
        for (int index = 0; index < steps; index++)
        {
            Vector3 before = gameObject.Transform.WorldPosition;
            MoveAlongAxisSingle(gameObject, step, scene);
            if ((gameObject.Transform.WorldPosition - before).Length() + 0.00001f < step.Length())
                break;
        }
    }

    private static void MoveAlongAxisSingle(GameObject gameObject, Vector3 displacement, Scene scene)
    {
        if (displacement.LengthSquared() <= 0.0000001f)
            return;

        Collider[] movingColliders = gameObject.Components
            .OfType<Collider>()
            .Where(collider => !collider.IsTrigger)
            .ToArray();

        if (movingColliders.Length == 0)
        {
            gameObject.Transform.SetWorldPosition(gameObject.Transform.WorldPosition + displacement);
            return;
        }

        float movingBottom = movingColliders.Select(collider => collider switch
        {
            CapsuleCollider capsule => CapsuleSegment(capsule).A.Y - CapsuleRadius(capsule),
            SphereCollider sphere => Center(sphere).Y - SphereRadius(sphere),
            BoxCollider box => Center(box).Y - BoxHalfSize(box).Y,
            _ => Center(collider).Y
        }).Min();
        bool horizontalMove = MathF.Abs(displacement.Y) <= 0.0000001f;

        Collider[] obstacles = scene.GameObjects
            .Where(other => other.IsActiveInHierarchy && !ReferenceEquals(other, gameObject))
            .SelectMany(other => other.Components)
            .OfType<Collider>()
            .Where(collider => !collider.IsTrigger &&
                (!horizontalMove || collider is not PlaneCollider) &&
                (!horizontalMove || collider is not BoxCollider box ||
                    movingBottom < Center(box).Y + BoxHalfSize(box).Y - 0.04f))
            .ToArray();

        Vector3 start = gameObject.Transform.WorldPosition;
        bool startedBlocked = HasSolidOverlap(movingColliders, obstacles);
        float startingPenetration = DeepestPenetration(movingColliders, obstacles);

        gameObject.Transform.SetWorldPosition(start + displacement);

        if (!HasSolidOverlap(movingColliders, obstacles))
            return;

        // An object already intersecting a solid must still be able to move out of it.
        if (startedBlocked &&
            DeepestPenetration(movingColliders, obstacles) <= startingPenetration + 0.00001f)
        {
            return;
        }


        if (startedBlocked)
        {
            gameObject.Transform.SetWorldPosition(start);
            return;
        }

        float clearAmount = 0.0f;
        float blockedAmount = 1.0f;

        for (int iteration = 0; iteration < 12; iteration++)
        {
            float amount = (clearAmount + blockedAmount) * 0.5f;
            gameObject.Transform.SetWorldPosition(start + displacement * amount);

            if (HasSolidOverlap(movingColliders, obstacles))
                blockedAmount = amount;
            else
                clearAmount = amount;
        }

        // Leave the colliders touching by the smallest practical amount so the
        // regular collision pass can issue enter/stay/exit events.
        gameObject.Transform.SetWorldPosition(start + displacement * blockedAmount);
    }

    private static bool HasSolidOverlap(Collider[] movingColliders, Collider[] obstacles) =>
        movingColliders.Any(moving => obstacles.Any(obstacle => CanInteract(moving, obstacle) && Overlaps(moving, obstacle)));

    private static float DeepestPenetration(Collider[] movingColliders, Collider[] obstacles)
    {
        float deepest = 0.0f;

        foreach (Collider moving in movingColliders)
        foreach (Collider obstacle in obstacles)
            if (CanInteract(moving, obstacle))
                deepest = MathF.Max(deepest, Penetration(moving, obstacle));

        return deepest;
    }

    private static float Penetration(Collider a, Collider b)
    {
        if (a is BoxCollider boxA && b is BoxCollider boxB)
        {
            Vector3 remaining = BoxHalfSize(boxA) + BoxHalfSize(boxB) -
                                Vector3.Abs(Center(boxA) - Center(boxB));
            return MathF.Max(0.0f, MathF.Min(remaining.X, MathF.Min(remaining.Y, remaining.Z)));
        }

        if (a is SphereCollider sphereA && b is SphereCollider sphereB)
            return MathF.Max(0.0f, SphereRadius(sphereA) + SphereRadius(sphereB) -
                                   Vector3.Distance(Center(sphereA), Center(sphereB)));

        if (a is CapsuleCollider capsuleA && b is CapsuleCollider capsuleB)
            return MathF.Max(0.0f, CapsuleRadius(capsuleA) + CapsuleRadius(capsuleB) -
                                   SegmentDistance(CapsuleSegment(capsuleA), CapsuleSegment(capsuleB)));

        if (a is CapsuleCollider capsule && b is SphereCollider sphereForCapsule)
            return MathF.Max(0.0f, CapsuleRadius(capsule) + SphereRadius(sphereForCapsule) -
                                   PointSegmentDistance(Center(sphereForCapsule), CapsuleSegment(capsule)));

        if (a is SphereCollider sphereForOtherCapsule && b is CapsuleCollider otherCapsule)
            return Penetration(otherCapsule, sphereForOtherCapsule);

        if (a is MeshCollider meshA)
            return MeshPenetration(meshA, b);
        if (b is MeshCollider meshB)
            return MeshPenetration(meshB, a);

        if (a is CapsuleCollider capsuleForBox && b is BoxCollider boxForCapsule)
            return CapsuleBoxPenetration(capsuleForBox, boxForCapsule);

        if (a is BoxCollider boxForOtherCapsule && b is CapsuleCollider otherCapsuleForBox)
            return CapsuleBoxPenetration(otherCapsuleForBox, boxForOtherCapsule);

        SphereCollider sphere = a as SphereCollider ?? (SphereCollider)b;
        BoxCollider box = a as BoxCollider ?? (BoxCollider)b;
        Vector3 boxCenter = Center(box);
        Vector3 half = BoxHalfSize(box);
        Vector3 closest = Vector3.Clamp(Center(sphere), boxCenter - half, boxCenter + half);
        return MathF.Max(0.0f, SphereRadius(sphere) - Vector3.Distance(Center(sphere), closest));
    }

    private static void Notify(ColliderPair pair, bool entering)
    {
        bool trigger = pair.A.IsTrigger || pair.B.IsTrigger;
        NotifyObject(pair.A.GameObject, pair.B.GameObject, trigger, entering);
        NotifyObject(pair.B.GameObject, pair.A.GameObject, trigger, entering);
    }

    private static void NotifyObject(GameObject owner, GameObject other, bool trigger, bool entering)
    {
        if (trigger && entering) owner.GetComponent<AnimatorTriggerZone>()?.NotifyEntry(other);
        if (trigger) owner.GetComponent<Interactable>()?.NotifyTrigger(other, entering);
        foreach (ScriptComponent script in owner.Components.OfType<ScriptComponent>())
            script.NotifyCollision(other, trigger, entering);
    }

    private static void NotifyStay(ColliderPair pair)
    {
        bool trigger = pair.A.IsTrigger || pair.B.IsTrigger;
        NotifyObjectStay(pair.A.GameObject, pair.B.GameObject, trigger);
        NotifyObjectStay(pair.B.GameObject, pair.A.GameObject, trigger);
    }

    private static void NotifyObjectStay(GameObject owner, GameObject other, bool trigger)
    {
        foreach (ScriptComponent script in owner.Components.OfType<ScriptComponent>())
            script.NotifyCollisionStay(other, trigger);
    }

    private static bool Overlaps(Collider a, Collider b)
    {
        if (a is MeshCollider meshA) return MeshOverlap(meshA, b);
        if (b is MeshCollider meshB) return MeshOverlap(meshB, a);

        if (a is CapsuleCollider capsuleA && b is CapsuleCollider capsuleB)
            return SegmentDistance(CapsuleSegment(capsuleA), CapsuleSegment(capsuleB)) <=
                   CapsuleRadius(capsuleA) + CapsuleRadius(capsuleB);
        if (a is CapsuleCollider capsule && b is SphereCollider sphereForCapsule)
            return PointSegmentDistance(Center(sphereForCapsule), CapsuleSegment(capsule)) <=
                   CapsuleRadius(capsule) + SphereRadius(sphereForCapsule);
        if (a is SphereCollider sphereForOtherCapsule && b is CapsuleCollider otherCapsule)
            return Overlaps(otherCapsule, sphereForOtherCapsule);
        if (a is CapsuleCollider capsuleForBox && b is BoxCollider boxForCapsule)
            return CapsuleBoxPenetration(capsuleForBox, boxForCapsule) > 0.0f;
        if (a is BoxCollider boxForOtherCapsule && b is CapsuleCollider otherCapsuleForBox)
            return Overlaps(otherCapsuleForBox, boxForOtherCapsule);

        if (a is SphereCollider sphereA && b is SphereCollider sphereB)
            return SphereSphere(sphereA, sphereB);

        if (a is BoxCollider boxA && b is BoxCollider boxB)
            return BoxBox(boxA, boxB);

        if (a is SphereCollider sphere && b is BoxCollider box)
            return SphereBox(sphere, box);

        if (a is BoxCollider otherBox && b is SphereCollider otherSphere)
            return SphereBox(otherSphere, otherBox);

        return false;
    }

    private static bool RemainsInContact(Collider a, Collider b)
    {
        const float persistence = 0.025f;

        if (a is MeshCollider meshA) return MeshOverlap(meshA, b, persistence);
        if (b is MeshCollider meshB) return MeshOverlap(meshB, a, persistence);

        if (a is CapsuleCollider capsuleA && b is CapsuleCollider capsuleB)
            return SegmentDistance(CapsuleSegment(capsuleA), CapsuleSegment(capsuleB)) <=
                   CapsuleRadius(capsuleA) + CapsuleRadius(capsuleB) + persistence;
        if (a is CapsuleCollider capsule && b is SphereCollider sphereForCapsule)
            return PointSegmentDistance(Center(sphereForCapsule), CapsuleSegment(capsule)) <=
                   CapsuleRadius(capsule) + SphereRadius(sphereForCapsule) + persistence;
        if (a is SphereCollider sphereForOtherCapsule && b is CapsuleCollider otherCapsule)
            return RemainsInContact(otherCapsule, sphereForOtherCapsule);
        if (a is CapsuleCollider capsuleForBox && b is BoxCollider boxForCapsule)
            return CapsuleBoxPenetration(capsuleForBox, boxForCapsule, persistence) > 0.0f;
        if (a is BoxCollider boxForOtherCapsule && b is CapsuleCollider otherCapsuleForBox)
            return CapsuleBoxPenetration(otherCapsuleForBox, boxForOtherCapsule, persistence) > 0.0f;

        if (a is SphereCollider sphereA && b is SphereCollider sphereB)
        {
            float radius = SphereRadius(sphereA) + SphereRadius(sphereB) + persistence;
            return Vector3.DistanceSquared(Center(sphereA), Center(sphereB)) <= radius * radius;
        }

        if (a is BoxCollider boxA && b is BoxCollider boxB)
        {
            Vector3 delta = Vector3.Abs(Center(boxA) - Center(boxB));
            Vector3 total = BoxHalfSize(boxA) + BoxHalfSize(boxB) + new Vector3(persistence);
            return delta.X <= total.X && delta.Y <= total.Y && delta.Z <= total.Z;
        }

        if (a is SphereCollider sphere && b is BoxCollider box)
            return SphereBox(sphere, box, persistence);
        if (a is BoxCollider otherBox && b is SphereCollider otherSphere)
            return SphereBox(otherSphere, otherBox, persistence);

        return false;
    }

    private static bool CanInteract(Collider a, Collider b)
    {
        int layerA = Math.Clamp(a.Layer, 0, 31);
        int layerB = Math.Clamp(b.Layer, 0, 31);
        uint bitA = 1u << layerA;
        uint bitB = 1u << layerB;
        return (a.CollisionMask & bitB) != 0 && (b.CollisionMask & bitA) != 0;
    }

    private static Vector3 Center(Collider collider) =>
        collider.GameObject.Transform.WorldPosition + collider.Center;

    private static Vector3 BoxHalfSize(BoxCollider box) =>
        Vector3.Abs(box.Size * box.GameObject.Transform.WorldScale) * 0.5f;

    private static float SphereRadius(SphereCollider sphere)
    {
        Vector3 scale = Vector3.Abs(sphere.GameObject.Transform.WorldScale);
        return MathF.Abs(sphere.Radius) * MathF.Max(scale.X, MathF.Max(scale.Y, scale.Z));
    }

    private static string Describe(Collider collider, Vector3 center)
    {
        if (collider is BoxCollider box)
            return $"Collider: {box.GameObject.Name} Box center={Format(center)} halfSize={Format(BoxHalfSize(box))}";

        if (collider is SphereCollider sphere)
            return $"Collider: {sphere.GameObject.Name} Sphere center={Format(center)} radius={SphereRadius(sphere):0.###}";
        if (collider is CapsuleCollider capsule)
            return $"Collider: {capsule.GameObject.Name} Capsule center={Format(center)} height={CapsuleHeight(capsule):0.###} radius={CapsuleRadius(capsule):0.###}";
        return $"Collider: {collider.GameObject.Name} Static Mesh";
    }

    private static string Format(Vector3 value) =>
        $"({value.X:0.###}, {value.Y:0.###}, {value.Z:0.###})";

    private static bool SphereSphere(SphereCollider a, SphereCollider b)
    {
        float radius = SphereRadius(a) + SphereRadius(b);
        return Vector3.DistanceSquared(Center(a), Center(b)) <= radius * radius;
    }

    private static bool BoxBox(BoxCollider a, BoxCollider b)
    {
        Vector3 delta = Vector3.Abs(Center(a) - Center(b));
        Vector3 total = BoxHalfSize(a) + BoxHalfSize(b);
        return delta.X <= total.X && delta.Y <= total.Y && delta.Z <= total.Z;
    }

    private static bool SphereBox(SphereCollider sphere, BoxCollider box, float tolerance = 0.0f)
    {
        Vector3 boxCenter = Center(box);
        Vector3 half = BoxHalfSize(box);
        Vector3 sphereCenter = Center(sphere);
        Vector3 closest = Vector3.Clamp(sphereCenter, boxCenter - half, boxCenter + half);
        float radius = SphereRadius(sphere) + tolerance;
        return Vector3.DistanceSquared(sphereCenter, closest) <= radius * radius;
    }

    private readonly record struct Segment(Vector3 A, Vector3 B);

    private static float CapsuleRadius(CapsuleCollider capsule)
    {
        Vector3 scale = Vector3.Abs(capsule.GameObject.Transform.WorldScale);
        return MathF.Abs(capsule.Radius) * MathF.Max(scale.X, scale.Z);
    }

    private static float CapsuleHeight(CapsuleCollider capsule) =>
        MathF.Max(CapsuleRadius(capsule) * 2.0f,
            MathF.Abs(capsule.Height * capsule.GameObject.Transform.WorldScale.Y));

    private static Segment CapsuleSegment(CapsuleCollider capsule)
    {
        Vector3 center = Center(capsule);
        float halfLine = MathF.Max(0.0f, CapsuleHeight(capsule) * 0.5f - CapsuleRadius(capsule));
        return new Segment(center - Vector3.UnitY * halfLine, center + Vector3.UnitY * halfLine);
    }

    private static float PointSegmentDistance(Vector3 point, Segment segment)
    {
        Vector3 delta = segment.B - segment.A;
        float lengthSquared = delta.LengthSquared();
        float amount = lengthSquared <= 0.000001f ? 0.0f :
            Math.Clamp(Vector3.Dot(point - segment.A, delta) / lengthSquared, 0.0f, 1.0f);
        return Vector3.Distance(point, segment.A + delta * amount);
    }

    private static float SegmentDistance(Segment first, Segment second)
    {
        Vector3 u = first.B - first.A;
        Vector3 v = second.B - second.A;
        Vector3 w = first.A - second.A;
        float a = Vector3.Dot(u, u), b = Vector3.Dot(u, v), c = Vector3.Dot(v, v);
        float d = Vector3.Dot(u, w), e = Vector3.Dot(v, w);
        float denominator = a * c - b * b;
        float s = denominator < 0.000001f ? 0.0f : Math.Clamp((b * e - c * d) / denominator, 0.0f, 1.0f);
        float t = c < 0.000001f ? 0.0f : Math.Clamp((b * s + e) / c, 0.0f, 1.0f);
        if (a > 0.000001f) s = Math.Clamp((b * t - d) / a, 0.0f, 1.0f);
        return Vector3.Distance(first.A + u * s, second.A + v * t);
    }

    private static float CapsuleBoxPenetration(CapsuleCollider capsule, BoxCollider box, float tolerance = 0.0f)
    {
        Segment segment = CapsuleSegment(capsule);
        Vector3 boxCenter = Center(box);
        Vector3 half = BoxHalfSize(box);
        float y = Math.Clamp(boxCenter.Y, segment.A.Y, segment.B.Y);
        Vector3 closestSegment = new(segment.A.X, y, segment.A.Z);
        Vector3 closestBox = Vector3.Clamp(closestSegment, boxCenter - half, boxCenter + half);
        return MathF.Max(0.0f, CapsuleRadius(capsule) + tolerance - Vector3.Distance(closestSegment, closestBox));
    }

    private static bool MeshOverlap(MeshCollider meshCollider, Collider other, float contactSkin = 0.015f)
    {
        if (other is MeshCollider) return false;
        foreach ((Vector3 a, Vector3 b, Vector3 c) in MeshTriangles(meshCollider))
        {
            if (other is SphereCollider sphere &&
                Vector3.DistanceSquared(Center(sphere), ClosestPointOnTriangle(Center(sphere), a, b, c)) <=
                (SphereRadius(sphere) + contactSkin) * (SphereRadius(sphere) + contactSkin)) return true;
            if (other is CapsuleCollider capsule)
            {
                Segment segment = CapsuleSegment(capsule);
                float radiusSquared = (CapsuleRadius(capsule) + contactSkin) * (CapsuleRadius(capsule) + contactSkin);
                for (int sample = 0; sample <= 6; sample++)
                {
                    Vector3 point = Vector3.Lerp(segment.A, segment.B, sample / 6.0f);
                    if (Vector3.DistanceSquared(point, ClosestPointOnTriangle(point, a, b, c)) <= radiusSquared)
                        return true;
                }
            }
            if (other is BoxCollider box)
            {
                if (TriangleIntersectsAabb(a, b, c, Center(box), BoxHalfSize(box) + new Vector3(contactSkin))) return true;
            }
        }
        return false;
    }

    private static float MeshPenetration(MeshCollider meshCollider, Collider other)
    {
        const float contactSkin = 0.015f;
        float deepest = 0.0f;
        foreach ((Vector3 a, Vector3 b, Vector3 c) in MeshTriangles(meshCollider))
        {
            if (other is SphereCollider sphere)
            {
                float depth = SphereRadius(sphere) + contactSkin -
                    Vector3.Distance(Center(sphere), ClosestPointOnTriangle(Center(sphere), a, b, c));
                deepest = MathF.Max(deepest, depth);
            }
            else if (other is CapsuleCollider capsule)
            {
                Segment segment = CapsuleSegment(capsule);
                for (int sample = 0; sample <= 8; sample++)
                {
                    Vector3 point = Vector3.Lerp(segment.A, segment.B, sample / 8.0f);
                    float depth = CapsuleRadius(capsule) + contactSkin -
                        Vector3.Distance(point, ClosestPointOnTriangle(point, a, b, c));
                    deepest = MathF.Max(deepest, depth);
                }
            }
            else if (other is BoxCollider box)
            {
                deepest = MathF.Max(deepest, BoxTrianglePenetration(box, a, b, c, contactSkin));
            }
        }
        return MathF.Max(0.0f, deepest);
    }

    private static float BoxTrianglePenetration(
        BoxCollider box,
        Vector3 a,
        Vector3 b,
        Vector3 c,
        float contactSkin)
    {
        Vector3 center = Center(box);
        Vector3 half = BoxHalfSize(box);
        Vector3 expandedHalf = half + new Vector3(contactSkin);
        if (!TriangleIntersectsAabb(a, b, c, center, expandedHalf))
            return 0.0f;

        Vector3 normal = Vector3.Cross(b - a, c - a);
        float normalLengthSquared = normal.LengthSquared();
        if (normalLengthSquared <= 0.0000001f)
            return 0.0f;

        normal /= MathF.Sqrt(normalLengthSquared);
        float projectedRadius =
            expandedHalf.X * MathF.Abs(normal.X) +
            expandedHalf.Y * MathF.Abs(normal.Y) +
            expandedHalf.Z * MathF.Abs(normal.Z);
        float planeDistance = MathF.Abs(Vector3.Dot(center - a, normal));
        return MathF.Max(0.0f, projectedRadius - planeDistance);
    }

    private static bool TriangleIntersectsAabb(Vector3 a, Vector3 b, Vector3 c, Vector3 center, Vector3 half)
    {
        Vector3 v0 = a - center, v1 = b - center, v2 = c - center;
        Vector3 e0 = v1 - v0, e1 = v2 - v1, e2 = v0 - v2;
        Vector3[] axes =
        {
            Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ,
            Vector3.Cross(e0, Vector3.UnitX), Vector3.Cross(e0, Vector3.UnitY), Vector3.Cross(e0, Vector3.UnitZ),
            Vector3.Cross(e1, Vector3.UnitX), Vector3.Cross(e1, Vector3.UnitY), Vector3.Cross(e1, Vector3.UnitZ),
            Vector3.Cross(e2, Vector3.UnitX), Vector3.Cross(e2, Vector3.UnitY), Vector3.Cross(e2, Vector3.UnitZ),
            Vector3.Cross(e0, e1)
        };
        foreach (Vector3 axis in axes)
        {
            if (axis.LengthSquared() <= 0.0000001f) continue;
            float p0 = Vector3.Dot(v0, axis), p1 = Vector3.Dot(v1, axis), p2 = Vector3.Dot(v2, axis);
            float radius = half.X * MathF.Abs(axis.X) + half.Y * MathF.Abs(axis.Y) + half.Z * MathF.Abs(axis.Z);
            if (MathF.Min(p0, MathF.Min(p1, p2)) > radius || MathF.Max(p0, MathF.Max(p1, p2)) < -radius)
                return false;
        }
        return true;
    }

    private static IEnumerable<(Vector3 A, Vector3 B, Vector3 C)> MeshTriangles(MeshCollider collider)
    {
        MeshRenderer? renderer = collider.GameObject.GetComponent<MeshRenderer>();
        if (renderer == null) yield break;
        Mesh mesh = renderer.MeshData;
        Vector3 scale = collider.GameObject.Transform.WorldScale;
        Vector3 rotation = collider.GameObject.Transform.WorldRotation;
        const float radians = MathF.PI / 180.0f;
        Quaternion orientation = Quaternion.CreateFromYawPitchRoll(rotation.Y * radians, rotation.X * radians, rotation.Z * radians);
        Vector3 origin = collider.GameObject.Transform.WorldPosition + collider.Center;
        Vector3 TransformVertex(uint index)
        {
            int offset = checked((int)index * Mesh.FloatsPerVertex);
            Vector3 local = new(mesh.VertexData[offset], mesh.VertexData[offset + 1], mesh.VertexData[offset + 2]);
            return origin + Vector3.Transform(local * scale, orientation);
        }
        MeshSubmesh section = renderer.SubmeshIndex >= 0
            ? renderer.GetSubmesh(0)
            : new MeshSubmesh("Collider", 0, mesh.Indices.Length);
        int end = section.IndexStart + section.IndexCount;
        for (int index = section.IndexStart; index + 2 < end; index += 3)
            yield return (TransformVertex(mesh.Indices[index]), TransformVertex(mesh.Indices[index + 1]), TransformVertex(mesh.Indices[index + 2]));
    }

    private static Vector3 ClosestPointOnTriangle(Vector3 point, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 ab = b - a, ac = c - a, ap = point - a;
        float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0 && d2 <= 0) return a;
        Vector3 bp = point - b;
        float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0 && d4 <= d3) return b;
        float vc = d1 * d4 - d3 * d2;
        if (vc <= 0 && d1 >= 0 && d3 <= 0) return a + ab * (d1 / (d1 - d3));
        Vector3 cp = point - c;
        float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0 && d5 <= d6) return c;
        float vb = d5 * d2 - d1 * d6;
        if (vb <= 0 && d2 >= 0 && d6 <= 0) return a + ac * (d2 / (d2 - d6));
        float va = d3 * d6 - d5 * d4;
        if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0)
            return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));
        float denominator = 1.0f / (va + vb + vc);
        return a + ab * (vb * denominator) + ac * (vc * denominator);
    }

    private readonly struct ColliderPair : IEquatable<ColliderPair>
    {
        public readonly Collider A;
        public readonly Collider B;

        public ColliderPair(Collider a, Collider b)
        {
            if (RuntimeHelpers.GetHashCode(a) <= RuntimeHelpers.GetHashCode(b))
            {
                A = a;
                B = b;
            }
            else
            {
                A = b;
                B = a;
            }
        }

        public bool Equals(ColliderPair other) => ReferenceEquals(A, other.A) && ReferenceEquals(B, other.B);
        public override bool Equals(object? obj) => obj is ColliderPair other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(RuntimeHelpers.GetHashCode(A), RuntimeHelpers.GetHashCode(B));
    }
}
