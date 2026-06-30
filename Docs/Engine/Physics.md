# Physics & Collision Loop Internals

This guide explains the inner workings of OIRF Engine's 2D collision detection and physics simulation pipeline, focusing on optimization techniques, collision checks, and MTV projection.

---

## 1. The Collision Pipeline

Every frame, the collision and physics simulations execute inside their respective systems:

```
[Game Loop Update]
       │
       ▼
 1. CollisionSystem.Update()
       │
       ├─► Broad-Phase: Populate Spatial Hash grid bucket cells
       │
       ├─► Narrow-Phase: Check layered masks & AABB intersections
       │
       ├─► MTV Detection: Call CollisionMath.TryComputeMTV()
       │
       └─► Dispatch: Fire CollisionStartEvent / CollisionEndEvent
       │
       ▼
 2. PhysicsSystem.Update()
       │
       ├─► Read Active Collisions list
       │
       ├─► Apply pushing adjustments to TransformComponent.Position
       │
       ├─► Project velocity vectors pointing into obstacles
       │
       └─► Integrate linear velocities & apply Friction drag
```

---

## 2. Broad-Phase Collision Optimization: Spatial Hashing

Checking collisions for every entity against all other entities scales quadratically ($O(N^2)$), which quickly bottlenecks rendering. OIRF Engine solves this using a **Spatial Hash** grid system:

1. **Cell Size**: The world is divided into square cells of width/height equal to `CellSize` (configurable via `PhysicsCvars.CellSize`, defaulting to 64).
2. **AABB Mapping**: In `Update()`, the system calculates the bounding box enclosing all of an entity's fixtures and maps the entity's index to every grid cell it intersects.
3. **Pair Testing**: The system only tests collisions between entities that share at least one cell bucket.
4. **Duplicate Safeguard**: The system stores tested pairs in a hash (`_testedPairs`) using a 64-bit combined index key to avoid duplicate narrow-phase calculations.

---

## 3. Narrow-Phase: Layer/Mask & Geometry Checks

For each cell bucket containing multiple entities, the system runs `CheckEntityPair()`:

### 1. Channel Filtering
Calculates bitmask overlaps:
```csharp
bool aSeesB = fixA.Masks.Overlaps(fixB.Layers);
bool bSeesA = fixB.Masks.Overlaps(fixA.Layers);
```
If neither entity's mask overlaps the other's layer, execution early-exits.

### 2. AABB Pre-filter
Performs axis-aligned bounding box checks:
```csharp
if (axMax < bxMin || axMin > bxMax || ayMax < byMin || ayMin > byMax)
    continue;
```

### 3. Detailed Shape Intersection (CollisionMath)
If AABBs overlap, the system calls `CollisionMath.TryComputeMTV(shapeA, posA, shapeB, posB, out Vector2 mtv)`. This routine uses the **Separating Axis Theorem (SAT)** for polygons/boxes, and distance projection equations for circles, returning the **Minimum Translation Vector (MTV)** required to separate the overlapping shapes.

---

## 4. Physics Integration & Resolution (`PhysicsSystem`)

The `PhysicsSystem` processes linear velocity integration and resolves active hard contacts:

### 1. Velocity Integration
For dynamic bodies (where `Static = false`):
$$\text{Position} \leftarrow \text{Position} + \text{Velocity} \times dt$$
$$\text{Velocity} \leftarrow \text{Velocity} \times (1.0 - \text{Clamp}(\text{Friction} \times dt, 0.0, 1.0))$$

### 2. Physical Push Back (Contact Resolution)
The system listens to contact updates. If two hard fixtures overlap:
* If the hit entity is `Static` (e.g. walls): the dynamic body resolves 100% of the MTV vector:
  $$\text{Position} \leftarrow \text{Position} + \text{MTV}$$
* If both are dynamic bodies: the displacement is split 50/50:
  $$\text{Position} \leftarrow \text{Position} + \text{MTV} \times 0.5$$

### 3. Velocity Projection
To prevent dynamic bodies from jittering or tunneling through walls when pushing against them:
* The system calculates the dot product between the velocity vector and the collision normal vector (normalized MTV).
* If the velocity points *into* the collision boundary ($\mathbf{v} \cdot \mathbf{n} < 0$), it cancels out the normal velocity vector components:
  $$\mathbf{v} \leftarrow \mathbf{v} - (\mathbf{v} \cdot \mathbf{n})\mathbf{n}$$
  This allows entities to slide smoothly along walls.
