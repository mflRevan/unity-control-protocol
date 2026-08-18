using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace UCP.Bridge.Tests
{
    /// <summary>
    /// Regression coverage for the serializer that turns arbitrary <see cref="IUCPScript"/> return
    /// values into JSON. The reflection walk used to be unbounded, so returning anything holding a
    /// UnityEngine math struct (Vector3.normalized returns another Vector3, forever) overflowed the
    /// stack -- which .NET cannot catch and which killed the editor process outright.
    /// </summary>
    public class MiniJsonSerializerTests
    {
        private static Dictionary<string, object> Roundtrip(object value)
        {
            var json = MiniJson.Serialize(new { value });
            var parsed = MiniJson.Deserialize(json) as Dictionary<string, object>;
            Assert.IsNotNull(parsed, $"Serializer produced unparseable JSON: {json}");
            return parsed;
        }

        [Test]
        public void SerializesVector3WithoutStackOverflow()
        {
            // The original crash repro: `return new { pos = Vector3.zero }` from a UCP script.
            var json = MiniJson.Serialize(new { pos = Vector3.zero });

            Assert.AreEqual("{\"pos\":{\"x\":0,\"y\":0,\"z\":0}}", json);
        }

        [Test]
        public void SerializesUnityMathStructsAsPlainShapes()
        {
            Assert.AreEqual("{\"x\":1,\"y\":2}", MiniJson.Serialize(new Vector2(1f, 2f)));
            Assert.AreEqual("{\"x\":1,\"y\":2,\"z\":3,\"w\":4}", MiniJson.Serialize(new Vector4(1f, 2f, 3f, 4f)));
            Assert.AreEqual("{\"x\":0,\"y\":0,\"z\":0,\"w\":1}", MiniJson.Serialize(Quaternion.identity));
            Assert.AreEqual("{\"r\":1,\"g\":0,\"b\":0,\"a\":1}", MiniJson.Serialize(Color.red));
            Assert.AreEqual("{\"x\":1,\"y\":2,\"z\":3}", MiniJson.Serialize(new Vector3Int(1, 2, 3)));
            Assert.AreEqual(
                "{\"x\":1,\"y\":2,\"width\":3,\"height\":4}",
                MiniJson.Serialize(new Rect(1f, 2f, 3f, 4f)));
            Assert.AreEqual(
                "{\"center\":{\"x\":0,\"y\":0,\"z\":0},\"size\":{\"x\":2,\"y\":2,\"z\":2}}",
                MiniJson.Serialize(new Bounds(Vector3.zero, Vector3.one * 2f)));
        }

        [Test]
        public void SerializesQuaternionNestedInAnonymousResult()
        {
            var parsed = Roundtrip(new { rot = Quaternion.Euler(0f, 90f, 0f), pos = Vector3.one });
            Assert.IsInstanceOf<Dictionary<string, object>>(parsed["value"]);
        }

        [Test]
        public void SerializesUnityObjectAsIdentityInsteadOfWalkingTheSceneGraph()
        {
            var go = new GameObject("MiniJsonProbe");
            try
            {
                // GameObject.transform.gameObject is a cycle; walking it never terminates.
                var parsed = Roundtrip(go);
                var identity = (Dictionary<string, object>)parsed["value"];

                Assert.AreEqual("MiniJsonProbe", identity["name"]);
                Assert.AreEqual("GameObject", identity["type"]);
                Assert.IsTrue(identity.ContainsKey("instanceId"));

                // Components are the same story via Component.gameObject.
                var componentJson = MiniJson.Serialize(go.transform);
                Assert.IsTrue(componentJson.Contains("\"type\":\"Transform\""), componentJson);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void DestroyedUnityObjectSerializesAsNull()
        {
            var go = new GameObject("MiniJsonDestroyed");
            UnityEngine.Object.DestroyImmediate(go);

            Assert.AreEqual("{\"value\":null}", MiniJson.Serialize(new { value = go }));
        }

        [Test]
        public void ReferenceCyclesAreBrokenInsteadOfRecursingForever()
        {
            var a = new Node { Label = "a" };
            var b = new Node { Label = "b", Next = a };
            a.Next = b;

            var json = MiniJson.Serialize(a);

            Assert.IsTrue(json.Contains("<ucp:cycle>"), json);
            Assert.IsNotNull(MiniJson.Deserialize(json));
        }

        [Test]
        public void SelfReferencingDictionaryIsBroken()
        {
            var dict = new Dictionary<string, object> { ["name"] = "root" };
            dict["self"] = dict;

            var json = MiniJson.Serialize(dict);

            Assert.IsTrue(json.Contains("<ucp:cycle>"), json);
            Assert.IsNotNull(MiniJson.Deserialize(json));
        }

        [Test]
        public void UnboundedComputedPropertyRecursionIsDepthCapped()
        {
            // Mirrors the Vector3.normalized shape for a type the serializer has no special case
            // for: every read allocates a fresh instance, so reference tracking cannot help and
            // only the depth cap prevents a stack overflow.
            var json = MiniJson.Serialize(new Fractal());

            Assert.IsTrue(json.Contains("<ucp:max-depth>"), json);
            Assert.IsNotNull(MiniJson.Deserialize(json));
        }

        [Test]
        public void ThrowingGetterDoesNotCorruptTheDocument()
        {
            var json = MiniJson.Serialize(new Explosive());

            Assert.IsFalse(json.Contains("boom"), json);
            var parsed = MiniJson.Deserialize(json) as Dictionary<string, object>;
            Assert.IsNotNull(parsed, json);
            Assert.AreEqual("ok", parsed["safe"]);
        }

        [Test]
        public void NonFiniteFloatsSerializeAsNullRatherThanInvalidJson()
        {
            // Degenerate bounds and zero-length normalize hand out NaN routinely; raw NaN/Infinity
            // are not valid JSON and made the whole response unparseable on the CLI side.
            var json = MiniJson.Serialize(new { a = float.NaN, b = float.PositiveInfinity, c = double.NaN });

            Assert.AreEqual("{\"a\":null,\"b\":null,\"c\":null}", json);
            Assert.IsNotNull(MiniJson.Deserialize(json));
        }

        [Test]
        public void UnsignedAndWideIntegersSerializeAsNumbers()
        {
            var json = MiniJson.Serialize(new { a = (uint)7, b = (ushort)8, c = (byte)9, d = 10UL });

            Assert.AreEqual("{\"a\":7,\"b\":8,\"c\":9,\"d\":10}", json);
        }

        [Test]
        public void NonListEnumerablesSerializeAsArrays()
        {
            var json = MiniJson.Serialize(new { items = new HashSet<int> { 1 } });

            Assert.AreEqual("{\"items\":[1]}", json);
        }

        [Test]
        public void EnumsStillSerializeAsIntegers()
        {
            Assert.AreEqual("{\"value\":2}", MiniJson.Serialize(new { value = SampleEnum.Two }));
        }

        [Test]
        public void ParserRejectsTruncatedInputInsteadOfHanging()
        {
            // A truncated string literal used to spin the reader on end-of-input forever, wedging
            // the editor's main thread.
            Assert.Throws<FormatException>(() => MiniJson.Deserialize("{\"a\": \"unterminated"));
        }

        [Test]
        public void ParserRejectsPathologicallyNestedInput()
        {
            var deep = new string('[', 1000);
            Assert.Throws<FormatException>(() => MiniJson.Deserialize(deep));
        }

        [Test]
        public void OrdinaryPayloadsAreUnchanged()
        {
            var json = MiniJson.Serialize(new Dictionary<string, object>
            {
                ["name"] = "cube",
                ["active"] = true,
                ["children"] = new List<object> { 1L, 2.5, null },
            });

            Assert.AreEqual("{\"name\":\"cube\",\"active\":true,\"children\":[1,2.5,null]}", json);
        }

        private enum SampleEnum
        {
            One = 1,
            Two = 2,
        }

        private sealed class Node
        {
            public string Label;
            public Node Next;
        }

        private sealed class Fractal
        {
            public Fractal Child => new Fractal();
        }

        private sealed class Explosive
        {
            public string Safe => "ok";
            public string Boom => throw new InvalidOperationException("boom");
        }
    }
}
