using System.Numerics;
using R2Engine.Editor.Scene;

static void Near(Vector3 actual, Vector3 expected, string label)
{
    if (Vector3.Distance(actual, expected) > 0.002f)
        throw new Exception($"{label}: {actual} != {expected}");
}

RuntimeInput.Configure(InputActionBinding.CreateDefaults());
RuntimeInput.Clear();
var scene = new Scene("Orbit test");
var mimi = scene.CreateGameObject("Mimi");
var controller = mimi.AddComponent<PlayerController>();
controller.Acceleration = controller.Deceleration = 10000;
var cameraObject = scene.CreateGameObject("Camera");
cameraObject.AddComponent<Camera>().IsPrimary = true;
cameraObject.SetParent(mimi, false);
var camera = cameraObject.Transform;
camera.Position = new Vector3(-1, 2, -4);
camera.Rotation = new Vector3(-15, 180, 0);
var collision = new CollisionSystem();
var originalOffset = camera.WorldPosition - mimi.Transform.WorldPosition;

// A full orbit leaves Mimi still and preserves camera height/radius.
RuntimeInput.SetControllerState("test", new float[] { 0, 0, 1 }, Array.Empty<bool>());
for (int i = 0; i < 180; i++) controller.Update(1f / 60, collision, scene);
Near(mimi.Transform.WorldPosition, Vector3.Zero, "stationary orbit position");
Near(mimi.Transform.WorldRotation, Vector3.Zero, "stationary orbit facing");
Near(camera.WorldPosition, originalOffset, "full orbit offset");

// Forward, right, backward, left at several camera headings.
foreach (float yaw in new[] { 0f, 90f, 180f, 270f })
foreach (var input in new[] { new Vector2(0, 1), new Vector2(1, 0), new Vector2(0, -1), new Vector2(-1, 0) })
{
    camera.SetWorldTransform(mimi.Transform.WorldPosition + originalOffset,
        new Vector3(-15, yaw, 0), Vector3.One);
    var before = mimi.Transform.WorldPosition;
    var offset = camera.WorldPosition - before;
    RuntimeInput.SetControllerState("test", new float[] { input.X, -input.Y, 0 }, Array.Empty<bool>());
    controller.Update(0.1f, collision, scene);
    float angle = yaw * MathF.PI / 180;
    var direction = new Vector3(-MathF.Sin(angle) * input.Y + MathF.Cos(angle) * input.X,
        0, -MathF.Cos(angle) * input.Y - MathF.Sin(angle) * input.X);
    Near(mimi.Transform.WorldPosition - before, direction * controller.MoveSpeed * 0.1f, "camera-relative movement");
    Near(camera.WorldPosition - mimi.Transform.WorldPosition, offset, "camera offset after facing change");
    Near(camera.WorldRotation, new Vector3(-15, yaw, 0), "camera yaw independent of character");
    float facing = mimi.Transform.WorldRotation.Y * MathF.PI / 180;
    Near(new Vector3(MathF.Sin(facing), 0, MathF.Cos(facing)), direction, "Mimi faces movement");
}
foreach (float yaw in new[] { 0f, 90f, 180f, 270f })
{
    controller.InvertCameraPitch = false;
    camera.SetWorldTransform(mimi.Transform.WorldPosition + originalOffset,
        new Vector3(-15, yaw, 0), Vector3.One);
    var playerBefore = mimi.Transform.WorldPosition;
    RuntimeInput.SetControllerState("test", new float[] { 0, 0, 0, -1 }, Array.Empty<bool>());
    for (int i = 0; i < 120; i++) controller.Update(1f / 60, collision, scene);
    Near(camera.WorldRotation, new Vector3(controller.CameraMaxPitch, yaw, 0), "upper pitch limit");
    RuntimeInput.SetControllerState("test", new float[] { 0, 0, 0, 1 }, Array.Empty<bool>());
    for (int i = 0; i < 120; i++) controller.Update(1f / 60, collision, scene);
    Near(camera.WorldRotation, new Vector3(controller.CameraMinPitch, yaw, 0), "lower pitch limit");
    float radius = (camera.WorldPosition - mimi.Transform.WorldPosition).Length();
    if (MathF.Abs(radius - originalOffset.Length()) > 0.002f) throw new Exception("Pitch changed orbit distance");
    Near(mimi.Transform.WorldPosition, playerBefore, "pitch leaves player stationary");
    controller.InvertCameraPitch = true;
    controller.Update(0.1f, collision, scene);
    Near(camera.WorldRotation, new Vector3(controller.CameraMinPitch + 9, yaw, 0), "inverted pitch");
}
Console.WriteLine("PASS: full orbit, 16 movement directions, child camera independence, pitch limits/inversion/radius at four headings.");

var testScene = new Scene("Camera collision");
var target = testScene.CreateGameObject("Target");
target.AddComponent<CapsuleCollider>();
var camObject = testScene.CreateGameObject("Camera");
camObject.SetParent(target, false);
camObject.AddComponent<Camera>().IsPrimary = true;
camObject.Transform.Position = new Vector3(0, 1, -4);
camObject.Transform.Rotation = new Vector3(0, 180, 0);
var pc = target.AddComponent<PlayerController>();
var wall = testScene.CreateGameObject("Thin wall");
wall.Transform.Position = new Vector3(0, 1, -2);
var box = wall.AddComponent<BoxCollider>();
box.Size = new Vector3(4, 4, 0.002f);
RuntimeInput.Clear();
float hit = collision.CameraSweep(testScene, target, camObject, new Vector3(0,1,0), new Vector3(0,1,-4), 0.25f);
if (MathF.Abs(hit - 1.749f) > 0.002f) throw new Exception("Thin wall sweep failed");
pc.Update(1f/60, collision, testScene);
if (camObject.Transform.WorldPosition.Z < -1.751f) throw new Exception("Camera not pulled in");
wall.IsActive = false;
pc.Update(1f/60, collision, testScene);
if (camObject.Transform.WorldPosition.Z <= -4 || camObject.Transform.WorldPosition.Z >= -1.749f) throw new Exception("Camera return did not ease outward");
for (int i=0;i<120;i++) pc.Update(1f/60, collision, testScene);
Near(camObject.Transform.WorldPosition, new Vector3(0,1,-4), "restored desired distance");
wall.IsActive = true;
box.IsTrigger = true;
if (collision.CameraSweep(testScene,target,camObject,new Vector3(0,1,0),new Vector3(0,1,-4),0.25f) != 4) throw new Exception("Trigger blocked camera");
wall.IsActive = false;
var ground = testScene.CreateGameObject("Ground");
ground.AddComponent<MeshRenderer>().Mesh = PrimitiveMesh.Plane;
ground.Transform.Scale = new Vector3(20,1,20);
ground.AddComponent<MeshCollider>();
float groundHit = collision.CameraSweep(testScene,target,camObject,new Vector3(0,1,0),new Vector3(0,-2,0),0.25f);
if(MathF.Abs(groundHit-0.75f)>0.002f) throw new Exception($"Terrain collision failed: {groundHit}");
Console.WriteLine("PASS: camera pull-in, smooth recovery, thin walls, terrain, ignored self/trigger/inactive colliders.");

var overlapScene=new Scene("Camera target beside wall");
var overlapTarget=overlapScene.CreateGameObject("Player");
var overlapCamera=overlapScene.CreateGameObject("Camera");
var frontWall=overlapScene.CreateGameObject("Front wall");
frontWall.Transform.Position=new(0,1,.1f);frontWall.AddComponent<BoxCollider>().Size=new(4,4,.002f);
float retreat=collision.CameraSweep(overlapScene,overlapTarget,overlapCamera,new(0,1,0),new(0,1,-4),.25f);
if(MathF.Abs(retreat-4)>.001f)throw new Exception($"Wall in front collapsed clear camera path behind player: {retreat}");
if(collision.CameraSweep(overlapScene,overlapTarget,overlapCamera,new(0,1,0),new(0,1,4),.25f)!=0)throw new Exception("Initial overlap swept into wall");
if(collision.CameraSweep(overlapScene,overlapTarget,overlapCamera,new(0,1,0),new(0,1,-.05f),.25f)!=0)throw new Exception("Overlapping endpoint accepted");
if(collision.CameraSweep(overlapScene,overlapTarget,overlapCamera,new(0,1,0),new(4,1,0),.25f)!=0)throw new Exception("Tangent overlap accepted");
var backWall=overlapScene.CreateGameObject("Back wall");backWall.Transform.Position=new(0,1,-2);backWall.AddComponent<BoxCollider>().Size=new(4,4,.002f);
if(MathF.Abs(collision.CameraSweep(overlapScene,overlapTarget,overlapCamera,new(0,1,0),new(0,1,-4),.25f)-1.749f)>.002f)throw new Exception("Front overlap hid rear obstruction");
backWall.IsActive=false;
overlapCamera.AddComponent<Camera>().IsPrimary=true;overlapCamera.Transform.Position=new(0,1,-4);overlapCamera.Transform.Rotation=new(0,180,0);
var overlapController=overlapTarget.AddComponent<PlayerController>();RuntimeInput.Clear();
for(int i=0;i<30;i++)overlapController.Update(1f/60,collision,overlapScene);
Near(overlapCamera.Transform.WorldPosition,new(0,1,-4),"Camera remains behind player beside front wall");
frontWall.Transform.Position=new(.1f,1,0);frontWall.Transform.Rotation.Y=90;
if(MathF.Abs(collision.CameraSweep(overlapScene,overlapTarget,overlapCamera,new(0,1,0),new(-4,1,0),.25f)-4)>.001f)throw new Exception("Rotated wall separating overlap failed");
frontWall.IsActive=false;
var nearbyCapsule=overlapScene.CreateGameObject("Capsule");nearbyCapsule.Transform.Position=new(0,1,.6f);nearbyCapsule.AddComponent<CapsuleCollider>();
if(collision.CameraSweep(overlapScene,overlapTarget,overlapCamera,new(0,1,0),new(0,1,-4),.25f)!=4)throw new Exception("Capsule separating overlap failed");
nearbyCapsule.IsActive=false;
var nearbyGround=overlapScene.CreateGameObject("Floor");nearbyGround.AddComponent<MeshRenderer>().Mesh=PrimitiveMesh.Plane;nearbyGround.AddComponent<MeshCollider>();
if(collision.CameraSweep(overlapScene,overlapTarget,overlapCamera,new(0,.1f,0),new(0,4.1f,0),.25f)!=4)throw new Exception("Mesh separating overlap failed");
Console.WriteLine("PASS: initial overlap separates without camera collapse; inward/tangent/endpoint guards, second wall, rotated box, capsule and mesh");
