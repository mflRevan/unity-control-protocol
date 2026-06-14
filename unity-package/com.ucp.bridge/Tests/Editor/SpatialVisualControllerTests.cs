using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace UCP.Bridge.Tests
{
    /// <summary>
    /// Edit-mode coverage for the spatial/visual controllers added for in-scene authoring:
    /// TransformController, SpatialController, ViewController, and the shared ObjectLocator.
    /// </summary>
    public class SpatialVisualControllerTests
    {
        private CommandRouter _router;
        private readonly List<GameObject> _spawned = new();

        [SetUp]
        public void SetUp()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            _router = new CommandRouter();
            TransformController.Register(_router);
            SpatialController.Register(_router);
            ViewController.Register(_router);
            _spawned.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned)
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        private GameObject Spawn(PrimitiveType type, string name, Vector3 pos)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.position = pos;
            _spawned.Add(go);
            return go;
        }

        private static Dictionary<string, object> Result(JsonRpcResponse r)
        {
            Assert.That(r.error, Is.Null, "RPC returned an error");
            return (Dictionary<string, object>)r.result;
        }

        private static float F(object o) => Convert.ToSingle(o);
        private static List<object> Vec(Dictionary<string, object> d, string key) => (List<object>)d[key];

        // --- Transform -----------------------------------------------------

        [Test]
        public void Transform_Move_WorldAbsoluteAndRelative()
        {
            var go = Spawn(PrimitiveType.Cube, "Mover", Vector3.zero);
            var id = go.GetId();

            var abs = Result(_router.Dispatch("transform/move", 1,
                "{\"instanceId\":" + id + ",\"position\":[5,0,0],\"space\":\"world\"}"));
            Assert.That(go.transform.position.x, Is.EqualTo(5f).Within(0.001f));
            Assert.That(F(Vec(abs, "position")[0]), Is.EqualTo(5f).Within(0.001f));

            _router.Dispatch("transform/move", 1,
                "{\"instanceId\":" + id + ",\"position\":[1,0,0],\"relative\":true}");
            Assert.That(go.transform.position.x, Is.EqualTo(6f).Within(0.001f));
        }

        [Test]
        public void Transform_Rotate_AbsoluteEulerWorld()
        {
            var go = Spawn(PrimitiveType.Cube, "Rotor", Vector3.zero);
            _router.Dispatch("transform/rotate", 1,
                "{\"instanceId\":" + go.GetId() + ",\"euler\":[0,90,0]}");
            Assert.That(go.transform.eulerAngles.y, Is.EqualTo(90f).Within(0.01f));
        }

        [Test]
        public void Transform_Scale_UniformAndNonUniform()
        {
            var go = Spawn(PrimitiveType.Cube, "Scaler", Vector3.zero);
            _router.Dispatch("transform/scale", 1, "{\"instanceId\":" + go.GetId() + ",\"uniform\":2}");
            Assert.That(go.transform.localScale, Is.EqualTo(Vector3.one * 2f));

            _router.Dispatch("transform/scale", 1, "{\"instanceId\":" + go.GetId() + ",\"scale\":[1,3,1],\"relative\":true}");
            Assert.That(go.transform.localScale.y, Is.EqualTo(6f).Within(0.001f));
        }

        [Test]
        public void Transform_LookAt_FacesWorldPoint()
        {
            var go = Spawn(PrimitiveType.Cube, "Looker", Vector3.zero);
            _router.Dispatch("transform/look-at", 1,
                "{\"instanceId\":" + go.GetId() + ",\"target\":[10,0,0]}");
            // forward should point along +X
            Assert.That(Vector3.Dot(go.transform.forward, Vector3.right), Is.GreaterThan(0.99f));
        }

        [Test]
        public void Transform_Get_BulkReadByIds()
        {
            var a = Spawn(PrimitiveType.Cube, "A", new Vector3(1, 0, 0));
            var b = Spawn(PrimitiveType.Cube, "B", new Vector3(2, 0, 0));
            var res = Result(_router.Dispatch("transform/get", 1,
                "{\"ids\":[" + a.GetId() + "," + b.GetId() + "]}"));
            Assert.That(Convert.ToInt32(res["count"]), Is.EqualTo(2));
        }

        // --- ObjectLocator -------------------------------------------------

        [Test]
        public void Locator_ResolvesByNameAndPath()
        {
            var root = Spawn(PrimitiveType.Cube, "Root", Vector3.zero);
            var child = GameObject.CreatePrimitive(PrimitiveType.Cube);
            child.name = "Child";
            child.transform.SetParent(root.transform, false);
            _spawned.Add(child);

            _router.Dispatch("transform/move", 1, "{\"name\":\"Child\",\"position\":[0,4,0]}");
            Assert.That(child.transform.position.y, Is.EqualTo(4f).Within(0.001f));

            _router.Dispatch("transform/move", 1, "{\"path\":\"Root/Child\",\"position\":[0,7,0]}");
            Assert.That(child.transform.position.y, Is.EqualTo(7f).Within(0.001f));
        }

        // --- Spatial -------------------------------------------------------

        [Test]
        public void Spatial_Raycast_HitsColliderBelow()
        {
            var ground = Spawn(PrimitiveType.Cube, "Ground", Vector3.zero);
            ground.transform.localScale = new Vector3(20, 1, 20);

            var res = Result(_router.Dispatch("physics/raycast", 1,
                "{\"origin\":[0,5,0],\"direction\":[0,-1,0]}"));
            Assert.That(Convert.ToBoolean(res["hit"]), Is.True);
            Assert.That(Convert.ToInt32(res["instanceId"]), Is.EqualTo(ground.GetId()));
        }

        [Test]
        public void Spatial_Overlap_FindsSphereOverlap()
        {
            var box = Spawn(PrimitiveType.Cube, "Box", Vector3.zero);
            var res = Result(_router.Dispatch("physics/overlap", 1,
                "{\"shape\":\"sphere\",\"center\":[0,0,0],\"radius\":2}"));
            Assert.That(Convert.ToInt32(res["count"]), Is.GreaterThanOrEqualTo(1));
            _ = box;
        }

        [Test]
        public void Spatial_Bounds_ReturnsWorldAabb()
        {
            var go = Spawn(PrimitiveType.Cube, "Bounded", new Vector3(3, 0, 0));
            var res = Result(_router.Dispatch("object/bounds", 1, "{\"instanceId\":" + go.GetId() + "}"));
            Assert.That(F(Vec(res, "center")[0]), Is.EqualTo(3f).Within(0.01f));
            Assert.That(Convert.ToBoolean(res["empty"]), Is.False);
        }

        [Test]
        public void Spatial_Ground_DropsObjectOntoSurface()
        {
            var ground = Spawn(PrimitiveType.Cube, "Floor", Vector3.zero);
            ground.transform.localScale = new Vector3(20, 1, 20); // top surface at y=0.5
            var cube = Spawn(PrimitiveType.Cube, "Falling", new Vector3(0, 8, 0));

            var res = Result(_router.Dispatch("spatial/ground", 1,
                "{\"instanceId\":" + cube.GetId() + ",\"apply\":true}"));
            Assert.That(Convert.ToBoolean(res["hit"]), Is.True);
            // Cube (half-height 0.5) should rest with its centre at surface(0.5) + 0.5 = 1.0
            Assert.That(cube.transform.position.y, Is.EqualTo(1f).Within(0.05f));
        }

        [Test]
        public void Spatial_Nearest_SortsByDistance()
        {
            Spawn(PrimitiveType.Cube, "Near", new Vector3(1, 0, 0));
            Spawn(PrimitiveType.Cube, "Mid", new Vector3(5, 0, 0));
            Spawn(PrimitiveType.Cube, "Far", new Vector3(20, 0, 0));

            var res = Result(_router.Dispatch("spatial/nearest", 1,
                "{\"point\":[0,0,0],\"max\":2}"));
            var objects = (List<object>)res["objects"];
            Assert.That(objects.Count, Is.EqualTo(2));
            var first = (Dictionary<string, object>)objects[0];
            Assert.That(first["name"].ToString(), Is.EqualTo("Near"));
        }

        // --- View ----------------------------------------------------------

        [Test]
        public void View_Isolate_ProducesPngForSingleView()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                Assert.Ignore("No graphics device (headless -nographics); skipping render test.");

            var go = Spawn(PrimitiveType.Cube, "Hero", Vector3.zero);
            var res = Result(_router.Dispatch("view/isolate", 1,
                "{\"instanceId\":" + go.GetId() + ",\"views\":[\"front\"],\"maxEdge\":128}"));

            Assert.That(res["encoding"].ToString(), Is.EqualTo("base64"));
            Assert.That(res["data"].ToString().Length, Is.GreaterThan(0));
            Assert.That(Convert.ToInt32(res["width"]), Is.EqualTo(128));
            // Isolation must restore the object's original layer.
            Assert.That(go.layer, Is.EqualTo(0));
        }
    }
}
