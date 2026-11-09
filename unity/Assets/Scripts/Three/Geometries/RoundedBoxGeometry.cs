using System;

namespace THREE
{
    /// <summary>three/examples/jsm/geometries/RoundedBoxGeometry.js (r170).</summary>
    public class RoundedBoxGeometry : BoxGeometry
    {
        private static readonly Vector3 _tempNormal = new Vector3();

        private static double Comp(Vector3 v, char a) => a == 'x' ? v.x : a == 'y' ? v.y : v.z;

        private static double getUv(Vector3 faceDirVector, Vector3 normal, char uvAxis, char projectionAxis, double radius, double sideLength)
        {
            double totArcLength = 2 * Math.PI * radius / 4;
            // length of the planar facet
            double centerLength = Math.Max(sideLength - 2 * radius, 0);
            double halfArc = Math.PI / 4;
            // Get the vector projected onto the Y plane
            _tempNormal.copy(normal);
            if (projectionAxis == 'x') _tempNormal.x = 0;
            else if (projectionAxis == 'y') _tempNormal.y = 0;
            else _tempNormal.z = 0;
            _tempNormal.normalize();
            // total amount of UV space alloted to a single arc
            double arcUvRatio = 0.5 * totArcLength / (totArcLength + centerLength);
            // the distance along one arc the point is at
            double arcAngleRatio = 1.0 - _tempNormal.angleTo(faceDirVector) / halfArc;
            if (JS.Sign(Comp(_tempNormal, uvAxis)) == 1) return arcAngleRatio * arcUvRatio;
            // total amount of UV space alloted to the plane between the arcs
            double lenUv = centerLength / (totArcLength + centerLength);
            return lenUv + arcUvRatio + arcUvRatio * (1.0 - arcAngleRatio);
        }

        public RoundedBoxGeometry(double width = 1, double height = 1, double depth = 1, int segments = 2, double radius = 0.1)
            : base(1, 1, 1, segments * 2 + 1, segments * 2 + 1, segments * 2 + 1)
        {
            type = "RoundedBoxGeometry";
            // ensure segments is odd so we have a plane connecting the rounded corners
            segments = segments * 2 + 1;
            // ensure radius isn't bigger than shortest side
            radius = JS.Min(width / 2, height / 2, depth / 2, radius);
            if (segments == 1) return;
            var geometry2 = toNonIndexed();
            index = null;
            attributes["position"] = geometry2.attributes.position;
            attributes["normal"] = geometry2.attributes.normal;
            attributes["uv"] = geometry2.attributes.uv;
            structureVersion++;
            var position = new Vector3();
            var normal = new Vector3();
            var box = new Vector3(width, height, depth).divideScalar(2).subScalar(radius);
            var positions = attributes.position.array;
            var normals = attributes.normal.array;
            var uvs = attributes.uv.array;
            int faceTris = positions.Length / 6;
            var faceDirVector = new Vector3();
            double halfSegmentSize = 0.5 / segments;
            for (int i = 0, j = 0; i < positions.Length; i += 3, j += 2)
            {
                position.fromArray(positions, i);
                normal.copy(position);
                normal.x -= JS.Sign(normal.x) * halfSegmentSize;
                normal.y -= JS.Sign(normal.y) * halfSegmentSize;
                normal.z -= JS.Sign(normal.z) * halfSegmentSize;
                normal.normalize();
                positions[i] = (float)(box.x * JS.Sign(position.x) + normal.x * radius);
                positions[i + 1] = (float)(box.y * JS.Sign(position.y) + normal.y * radius);
                positions[i + 2] = (float)(box.z * JS.Sign(position.z) + normal.z * radius);
                normals[i] = (float)normal.x;
                normals[i + 1] = (float)normal.y;
                normals[i + 2] = (float)normal.z;
                int side = i / faceTris;
                switch (side)
                {
                    case 0:
                        faceDirVector.set(1, 0, 0);
                        uvs[j] = (float)getUv(faceDirVector, normal, 'z', 'y', radius, depth);
                        uvs[j + 1] = (float)(1.0 - getUv(faceDirVector, normal, 'y', 'z', radius, height));
                        break;
                    case 1:
                        faceDirVector.set(-1, 0, 0);
                        uvs[j] = (float)(1.0 - getUv(faceDirVector, normal, 'z', 'y', radius, depth));
                        uvs[j + 1] = (float)(1.0 - getUv(faceDirVector, normal, 'y', 'z', radius, height));
                        break;
                    case 2:
                        faceDirVector.set(0, 1, 0);
                        uvs[j] = (float)(1.0 - getUv(faceDirVector, normal, 'x', 'z', radius, width));
                        uvs[j + 1] = (float)getUv(faceDirVector, normal, 'z', 'x', radius, depth);
                        break;
                    case 3:
                        faceDirVector.set(0, -1, 0);
                        uvs[j] = (float)(1.0 - getUv(faceDirVector, normal, 'x', 'z', radius, width));
                        uvs[j + 1] = (float)(1.0 - getUv(faceDirVector, normal, 'z', 'x', radius, depth));
                        break;
                    case 4:
                        faceDirVector.set(0, 0, 1);
                        uvs[j] = (float)(1.0 - getUv(faceDirVector, normal, 'x', 'y', radius, width));
                        uvs[j + 1] = (float)(1.0 - getUv(faceDirVector, normal, 'y', 'x', radius, height));
                        break;
                    case 5:
                        faceDirVector.set(0, 0, -1);
                        uvs[j] = (float)getUv(faceDirVector, normal, 'x', 'y', radius, width);
                        uvs[j + 1] = (float)(1.0 - getUv(faceDirVector, normal, 'y', 'x', radius, height));
                        break;
                }
            }
        }
    }
}
