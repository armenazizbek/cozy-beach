using System;
using System.Collections.Generic;

namespace THREE
{
    /// <summary>Shared helpers for building geometry the way Three.js's generators do.</summary>
    internal class GeoBuilder
    {
        public readonly List<double> vertices = new List<double>();
        public readonly List<double> normals = new List<double>();
        public readonly List<double> uvs = new List<double>();
        public readonly List<int> indices = new List<int>();

        public void Apply(BufferGeometry g, bool withIndex = true)
        {
            if (withIndex) g.setIndex(indices);
            g.setAttribute("position", new Float32BufferAttribute(vertices, 3));
            g.setAttribute("normal", new Float32BufferAttribute(normals, 3));
            g.setAttribute("uv", new Float32BufferAttribute(uvs, 2));
        }
    }

    public class BoxGeometry : BufferGeometry
    {
        public readonly double width, height, depth;

        public BoxGeometry(double width = 1, double height = 1, double depth = 1, int widthSegments = 1, int heightSegments = 1, int depthSegments = 1)
        {
            type = "BoxGeometry";
            this.width = width; this.height = height; this.depth = depth;
            var b = new GeoBuilder();
            int numberOfVertices = 0, groupStart = 0;
            void BuildPlane(int u, int v, int w, double udir, double vdir, double pw, double ph, double pd, int gridX, int gridY, int materialIndex)
            {
                double segmentWidth = pw / gridX, segmentHeight = ph / gridY;
                double widthHalf = pw / 2, heightHalf = ph / 2, depthHalf = pd / 2;
                int gridX1 = gridX + 1, gridY1 = gridY + 1;
                int vertexCounter = 0, groupCount = 0;
                var vec = new double[3];
                for (int iy = 0; iy < gridY1; iy++)
                {
                    double y = iy * segmentHeight - heightHalf;
                    for (int ix = 0; ix < gridX1; ix++)
                    {
                        double x = ix * segmentWidth - widthHalf;
                        vec[u] = x * udir; vec[v] = y * vdir; vec[w] = depthHalf;
                        b.vertices.Add(vec[0]); b.vertices.Add(vec[1]); b.vertices.Add(vec[2]);
                        vec[u] = 0; vec[v] = 0; vec[w] = pd > 0 ? 1 : -1;
                        b.normals.Add(vec[0]); b.normals.Add(vec[1]); b.normals.Add(vec[2]);
                        b.uvs.Add((double)ix / gridX);
                        b.uvs.Add(1 - (double)iy / gridY);
                        vertexCounter++;
                    }
                }
                for (int iy = 0; iy < gridY; iy++)
                for (int ix = 0; ix < gridX; ix++)
                {
                    int a = numberOfVertices + ix + gridX1 * iy;
                    int bb = numberOfVertices + ix + gridX1 * (iy + 1);
                    int c = numberOfVertices + (ix + 1) + gridX1 * (iy + 1);
                    int d = numberOfVertices + (ix + 1) + gridX1 * iy;
                    b.indices.Add(a); b.indices.Add(bb); b.indices.Add(d);
                    b.indices.Add(bb); b.indices.Add(c); b.indices.Add(d);
                    groupCount += 6;
                }
                addGroup(groupStart, groupCount, materialIndex);
                groupStart += groupCount;
                numberOfVertices += vertexCounter;
            }
            // x=0 y=1 z=2
            BuildPlane(2, 1, 0, -1, -1, depth, height, width, depthSegments, heightSegments, 0);
            BuildPlane(2, 1, 0, 1, -1, depth, height, -width, depthSegments, heightSegments, 1);
            BuildPlane(0, 2, 1, 1, 1, width, depth, height, widthSegments, depthSegments, 2);
            BuildPlane(0, 2, 1, 1, -1, width, depth, -height, widthSegments, depthSegments, 3);
            BuildPlane(0, 1, 2, 1, -1, width, height, depth, widthSegments, heightSegments, 4);
            BuildPlane(0, 1, 2, -1, -1, width, height, -depth, widthSegments, heightSegments, 5);
            b.Apply(this);
        }
    }

    public class PlaneGeometry : BufferGeometry
    {
        public readonly double width, height;

        public PlaneGeometry(double width = 1, double height = 1, int widthSegments = 1, int heightSegments = 1)
        {
            type = "PlaneGeometry";
            this.width = width; this.height = height;
            double widthHalf = width / 2, heightHalf = height / 2;
            int gridX = widthSegments, gridY = heightSegments, gridX1 = gridX + 1, gridY1 = gridY + 1;
            double segmentWidth = width / gridX, segmentHeight = height / gridY;
            var b = new GeoBuilder();
            for (int iy = 0; iy < gridY1; iy++)
            {
                double y = iy * segmentHeight - heightHalf;
                for (int ix = 0; ix < gridX1; ix++)
                {
                    double x = ix * segmentWidth - widthHalf;
                    b.vertices.Add(x); b.vertices.Add(-y); b.vertices.Add(0);
                    b.normals.Add(0); b.normals.Add(0); b.normals.Add(1);
                    b.uvs.Add((double)ix / gridX);
                    b.uvs.Add(1 - (double)iy / gridY);
                }
            }
            for (int iy = 0; iy < gridY; iy++)
            for (int ix = 0; ix < gridX; ix++)
            {
                int a = ix + gridX1 * iy, bb = ix + gridX1 * (iy + 1), c = ix + 1 + gridX1 * (iy + 1), d = ix + 1 + gridX1 * iy;
                b.indices.Add(a); b.indices.Add(bb); b.indices.Add(d);
                b.indices.Add(bb); b.indices.Add(c); b.indices.Add(d);
            }
            b.Apply(this);
        }
    }

    public class CircleGeometry : BufferGeometry
    {
        public CircleGeometry(double radius = 1, int segments = 32, double thetaStart = 0, double thetaLength = Math.PI * 2)
        {
            type = "CircleGeometry";
            segments = Math.Max(3, segments);
            var b = new GeoBuilder();
            b.vertices.Add(0); b.vertices.Add(0); b.vertices.Add(0);
            b.normals.Add(0); b.normals.Add(0); b.normals.Add(1);
            b.uvs.Add(0.5); b.uvs.Add(0.5);
            for (int s = 0, i = 3; s <= segments; s++, i += 3)
            {
                double segment = thetaStart + (double)s / segments * thetaLength;
                double vx = radius * Math.Cos(segment), vy = radius * Math.Sin(segment);
                b.vertices.Add(vx); b.vertices.Add(vy); b.vertices.Add(0);
                b.normals.Add(0); b.normals.Add(0); b.normals.Add(1);
                b.uvs.Add((vx / radius + 1) / 2);
                b.uvs.Add((vy / radius + 1) / 2);
            }
            for (int i = 1; i <= segments; i++)
            {
                b.indices.Add(i); b.indices.Add(i + 1); b.indices.Add(0);
            }
            b.Apply(this);
        }
    }

    public class RingGeometry : BufferGeometry
    {
        public RingGeometry(double innerRadius = 0.5, double outerRadius = 1, int thetaSegments = 32, int phiSegments = 1, double thetaStart = 0, double thetaLength = Math.PI * 2)
        {
            type = "RingGeometry";
            thetaSegments = Math.Max(3, thetaSegments);
            phiSegments = Math.Max(1, phiSegments);
            var b = new GeoBuilder();
            double radius = innerRadius, radiusStep = (outerRadius - innerRadius) / phiSegments;
            for (int j = 0; j <= phiSegments; j++)
            {
                for (int i = 0; i <= thetaSegments; i++)
                {
                    double segment = thetaStart + (double)i / thetaSegments * thetaLength;
                    double vx = radius * Math.Cos(segment), vy = radius * Math.Sin(segment);
                    b.vertices.Add(vx); b.vertices.Add(vy); b.vertices.Add(0);
                    b.normals.Add(0); b.normals.Add(0); b.normals.Add(1);
                    b.uvs.Add((vx / outerRadius + 1) / 2);
                    b.uvs.Add((vy / outerRadius + 1) / 2);
                }
                radius += radiusStep;
            }
            for (int j = 0; j < phiSegments; j++)
            {
                int thetaSegmentLevel = j * (thetaSegments + 1);
                for (int i = 0; i < thetaSegments; i++)
                {
                    int segment = i + thetaSegmentLevel;
                    int a = segment, bb = segment + thetaSegments + 1, c = segment + thetaSegments + 2, d = segment + 1;
                    b.indices.Add(a); b.indices.Add(bb); b.indices.Add(d);
                    b.indices.Add(bb); b.indices.Add(c); b.indices.Add(d);
                }
            }
            b.Apply(this);
        }
    }

    public class CylinderGeometry : BufferGeometry
    {
        public CylinderGeometry(double radiusTop = 1, double radiusBottom = 1, double height = 1, int radialSegments = 32, int heightSegments = 1,
            bool openEnded = false, double thetaStart = 0, double thetaLength = Math.PI * 2)
        {
            type = "CylinderGeometry";
            var b = new GeoBuilder();
            int index = 0, groupStart = 0;
            var indexArray = new List<int[]>();
            double halfHeight = height / 2;
            // torso
            {
                int groupCount = 0;
                double slope = (radiusBottom - radiusTop) / height;
                for (int y = 0; y <= heightSegments; y++)
                {
                    var row = new int[radialSegments + 1];
                    double v = (double)y / heightSegments;
                    double radius = v * (radiusBottom - radiusTop) + radiusTop;
                    for (int x = 0; x <= radialSegments; x++)
                    {
                        double u = (double)x / radialSegments;
                        double theta = u * thetaLength + thetaStart;
                        double sinTheta = Math.Sin(theta), cosTheta = Math.Cos(theta);
                        b.vertices.Add(radius * sinTheta); b.vertices.Add(-v * height + halfHeight); b.vertices.Add(radius * cosTheta);
                        double nx = sinTheta, ny = slope, nz = cosTheta, nl = Math.Sqrt(nx * nx + ny * ny + nz * nz);
                        b.normals.Add(nx / nl); b.normals.Add(ny / nl); b.normals.Add(nz / nl);
                        b.uvs.Add(u); b.uvs.Add(1 - v);
                        row[x] = index++;
                    }
                    indexArray.Add(row);
                }
                for (int x = 0; x < radialSegments; x++)
                for (int y = 0; y < heightSegments; y++)
                {
                    int a = indexArray[y][x], bb = indexArray[y + 1][x], c = indexArray[y + 1][x + 1], d = indexArray[y][x + 1];
                    if (radiusTop > 0 || y != 0) { b.indices.Add(a); b.indices.Add(bb); b.indices.Add(d); groupCount += 3; }
                    if (radiusBottom > 0 || y != heightSegments - 1) { b.indices.Add(bb); b.indices.Add(c); b.indices.Add(d); groupCount += 3; }
                }
                addGroup(groupStart, groupCount, 0);
                groupStart += groupCount;
            }
            void Cap(bool top)
            {
                int centerIndexStart = index;
                double radius = top ? radiusTop : radiusBottom;
                double sign = top ? 1 : -1;
                int groupCount = 0;
                for (int x = 1; x <= radialSegments; x++)
                {
                    b.vertices.Add(0); b.vertices.Add(halfHeight * sign); b.vertices.Add(0);
                    b.normals.Add(0); b.normals.Add(sign); b.normals.Add(0);
                    b.uvs.Add(0.5); b.uvs.Add(0.5);
                    index++;
                }
                int centerIndexEnd = index;
                for (int x = 0; x <= radialSegments; x++)
                {
                    double u = (double)x / radialSegments;
                    double theta = u * thetaLength + thetaStart;
                    double cosTheta = Math.Cos(theta), sinTheta = Math.Sin(theta);
                    b.vertices.Add(radius * sinTheta); b.vertices.Add(halfHeight * sign); b.vertices.Add(radius * cosTheta);
                    b.normals.Add(0); b.normals.Add(sign); b.normals.Add(0);
                    b.uvs.Add(cosTheta * 0.5 + 0.5); b.uvs.Add(sinTheta * 0.5 * sign + 0.5);
                    index++;
                }
                for (int x = 0; x < radialSegments; x++)
                {
                    int c = centerIndexStart + x, i = centerIndexEnd + x;
                    if (top) { b.indices.Add(i); b.indices.Add(i + 1); b.indices.Add(c); }
                    else { b.indices.Add(i + 1); b.indices.Add(i); b.indices.Add(c); }
                    groupCount += 3;
                }
                addGroup(groupStart, groupCount, top ? 1 : 2);
                groupStart += groupCount;
            }
            if (!openEnded)
            {
                if (radiusTop > 0) Cap(true);
                if (radiusBottom > 0) Cap(false);
            }
            b.Apply(this);
        }
    }

    public class ConeGeometry : CylinderGeometry
    {
        public ConeGeometry(double radius = 1, double height = 1, int radialSegments = 32, int heightSegments = 1, bool openEnded = false,
            double thetaStart = 0, double thetaLength = Math.PI * 2)
            : base(0, radius, height, radialSegments, heightSegments, openEnded, thetaStart, thetaLength)
        {
            type = "ConeGeometry";
        }
    }

    public class SphereGeometry : BufferGeometry
    {
        public SphereGeometry(double radius = 1, int widthSegments = 32, int heightSegments = 16, double phiStart = 0, double phiLength = Math.PI * 2,
            double thetaStart = 0, double thetaLength = Math.PI)
        {
            type = "SphereGeometry";
            widthSegments = Math.Max(3, widthSegments);
            heightSegments = Math.Max(2, heightSegments);
            double thetaEnd = Math.Min(thetaStart + thetaLength, Math.PI);
            int index = 0;
            var grid = new List<int[]>();
            var b = new GeoBuilder();
            for (int iy = 0; iy <= heightSegments; iy++)
            {
                var row = new int[widthSegments + 1];
                double v = (double)iy / heightSegments;
                double uOffset = 0;
                if (iy == 0 && thetaStart == 0) uOffset = 0.5 / widthSegments;
                else if (iy == heightSegments && thetaEnd == Math.PI) uOffset = -0.5 / widthSegments;
                for (int ix = 0; ix <= widthSegments; ix++)
                {
                    double u = (double)ix / widthSegments;
                    double vx = -radius * Math.Cos(phiStart + u * phiLength) * Math.Sin(thetaStart + v * thetaLength);
                    double vy = radius * Math.Cos(thetaStart + v * thetaLength);
                    double vz = radius * Math.Sin(phiStart + u * phiLength) * Math.Sin(thetaStart + v * thetaLength);
                    b.vertices.Add(vx); b.vertices.Add(vy); b.vertices.Add(vz);
                    double l = Math.Sqrt(vx * vx + vy * vy + vz * vz);
                    if (l == 0) l = 1;
                    b.normals.Add(vx / l); b.normals.Add(vy / l); b.normals.Add(vz / l);
                    b.uvs.Add(u + uOffset); b.uvs.Add(1 - v);
                    row[ix] = index++;
                }
                grid.Add(row);
            }
            for (int iy = 0; iy < heightSegments; iy++)
            for (int ix = 0; ix < widthSegments; ix++)
            {
                int a = grid[iy][ix + 1], bb = grid[iy][ix], c = grid[iy + 1][ix], d = grid[iy + 1][ix + 1];
                if (iy != 0 || thetaStart > 0) { b.indices.Add(a); b.indices.Add(bb); b.indices.Add(d); }
                if (iy != heightSegments - 1 || thetaEnd < Math.PI) { b.indices.Add(bb); b.indices.Add(c); b.indices.Add(d); }
            }
            b.Apply(this);
        }
    }

    public class TorusGeometry : BufferGeometry
    {
        public TorusGeometry(double radius = 1, double tube = 0.4, int radialSegments = 12, int tubularSegments = 48, double arc = Math.PI * 2)
        {
            type = "TorusGeometry";
            var b = new GeoBuilder();
            for (int j = 0; j <= radialSegments; j++)
            for (int i = 0; i <= tubularSegments; i++)
            {
                double u = (double)i / tubularSegments * arc, v = (double)j / radialSegments * Math.PI * 2;
                double vx = (radius + tube * Math.Cos(v)) * Math.Cos(u);
                double vy = (radius + tube * Math.Cos(v)) * Math.Sin(u);
                double vz = tube * Math.Sin(v);
                b.vertices.Add(vx); b.vertices.Add(vy); b.vertices.Add(vz);
                double cx = radius * Math.Cos(u), cy = radius * Math.Sin(u);
                double nx = vx - cx, ny = vy - cy, nz = vz, nl = Math.Sqrt(nx * nx + ny * ny + nz * nz);
                if (nl == 0) nl = 1;
                b.normals.Add(nx / nl); b.normals.Add(ny / nl); b.normals.Add(nz / nl);
                b.uvs.Add((double)i / tubularSegments); b.uvs.Add((double)j / radialSegments);
            }
            for (int j = 1; j <= radialSegments; j++)
            for (int i = 1; i <= tubularSegments; i++)
            {
                int a = (tubularSegments + 1) * j + i - 1, bb = (tubularSegments + 1) * (j - 1) + i - 1;
                int c = (tubularSegments + 1) * (j - 1) + i, d = (tubularSegments + 1) * j + i;
                b.indices.Add(a); b.indices.Add(bb); b.indices.Add(d);
                b.indices.Add(bb); b.indices.Add(c); b.indices.Add(d);
            }
            b.Apply(this);
        }
    }

    public class LatheGeometry : BufferGeometry
    {
        public LatheGeometry(IList<Vector2> points, int segments = 12, double phiStart = 0, double phiLength = Math.PI * 2)
        {
            type = "LatheGeometry";
            phiLength = MathUtils.clamp(phiLength, 0, Math.PI * 2);
            var b = new GeoBuilder();
            var initNormals = new List<double>();
            double inverseSegments = 1.0 / segments;
            double dx = 0, dy = 0, prevx = 0, prevy = 0;
            var curNormal = new Vector3();
            var prevNormal = new Vector3();
            for (int j = 0; j <= points.Count - 1; j++)
            {
                if (j == 0)
                {
                    dx = points[j + 1].x - points[j].x;
                    dy = points[j + 1].y - points[j].y;
                    double nx = dy * 1.0, ny = -dx, nz = dy * 0.0;
                    prevNormal.set(nx, ny, nz);
                    curNormal.set(nx, ny, nz).normalize();
                    initNormals.Add(curNormal.x); initNormals.Add(curNormal.y); initNormals.Add(curNormal.z);
                }
                else if (j == points.Count - 1)
                {
                    initNormals.Add(prevNormal.x); initNormals.Add(prevNormal.y); initNormals.Add(prevNormal.z);
                }
                else
                {
                    dx = points[j + 1].x - points[j].x;
                    dy = points[j + 1].y - points[j].y;
                    double nx = dy * 1.0, ny = -dx, nz = dy * 0.0;
                    curNormal.set(nx, ny, nz);
                    curNormal.x += prevNormal.x; curNormal.y += prevNormal.y; curNormal.z += prevNormal.z;
                    curNormal.normalize();
                    initNormals.Add(curNormal.x); initNormals.Add(curNormal.y); initNormals.Add(curNormal.z);
                    prevNormal.set(nx, ny, nz);
                }
            }
            for (int i = 0; i <= segments; i++)
            {
                double phi = phiStart + i * inverseSegments * phiLength;
                double sin = Math.Sin(phi), cos = Math.Cos(phi);
                for (int j = 0; j <= points.Count - 1; j++)
                {
                    b.vertices.Add(points[j].x * sin); b.vertices.Add(points[j].y); b.vertices.Add(points[j].x * cos);
                    b.uvs.Add((double)i / segments); b.uvs.Add((double)j / (points.Count - 1));
                    double x = initNormals[3 * j] * sin, y = initNormals[3 * j + 1], z = initNormals[3 * j] * cos;
                    b.normals.Add(x); b.normals.Add(y); b.normals.Add(z);
                }
            }
            for (int i = 0; i < segments; i++)
            for (int j = 0; j < points.Count - 1; j++)
            {
                int bas = j + i * points.Count;
                int a = bas, bb = bas + points.Count, c = bas + points.Count + 1, d = bas + 1;
                b.indices.Add(a); b.indices.Add(bb); b.indices.Add(d);
                b.indices.Add(c); b.indices.Add(d); b.indices.Add(bb);
            }
            b.Apply(this);
        }
    }

    public class CapsuleGeometry : BufferGeometry
    {
        /// <summary>Three r170's capsule: a lathe of a path with two arcs and a straight middle.</summary>
        public CapsuleGeometry(double radius = 1, double length = 1, int capSegments = 4, int radialSegments = 8)
        {
            type = "CapsuleGeometry";
            var path = new Path();
            path.absarc(0, -length / 2, radius, Math.PI * 1.5, 0);
            path.absarc(0, length / 2, radius, 0, Math.PI * 0.5);
            var pts = path.getPoints(capSegments);
            var lathe = new LatheGeometry(pts, radialSegments);
            copy(lathe);
        }
    }

    public class PolyhedronGeometry : BufferGeometry
    {
        public PolyhedronGeometry(double[] vertices, int[] indices, double radius = 1, int detail = 0)
        {
            type = "PolyhedronGeometry";
            var vertexBuffer = new List<double>();
            var uvBuffer = new List<double>();
            void PushVertex(Vector3 v) { vertexBuffer.Add(v.x); vertexBuffer.Add(v.y); vertexBuffer.Add(v.z); }
            Vector3 GetVertexByIndex(int i) => new Vector3(vertices[i * 3], vertices[i * 3 + 1], vertices[i * 3 + 2]);
            void SubdivideFace(Vector3 a, Vector3 b, Vector3 c, int det)
            {
                int cols = det + 1;
                var v = new List<List<Vector3>>();
                for (int i = 0; i <= cols; i++)
                {
                    v.Add(new List<Vector3>());
                    var aj = a.clone().lerp(c, (double)i / cols);
                    var bj = b.clone().lerp(c, (double)i / cols);
                    int rows = cols - i;
                    for (int j = 0; j <= rows; j++)
                    {
                        if (j == 0 && i == cols) v[i].Add(aj);
                        else v[i].Add(aj.clone().lerp(bj, (double)j / rows));
                    }
                }
                for (int i = 0; i < cols; i++)
                for (int j = 0; j < 2 * (cols - i) - 1; j++)
                {
                    int k = j / 2;
                    if (j % 2 == 0)
                    {
                        PushVertex(v[i][k + 1]);
                        PushVertex(v[i + 1][k]);
                        PushVertex(v[i][k]);
                    }
                    else
                    {
                        PushVertex(v[i][k + 1]);
                        PushVertex(v[i + 1][k + 1]);
                        PushVertex(v[i + 1][k]);
                    }
                }
            }
            for (int i = 0; i < indices.Length; i += 3)
                SubdivideFace(GetVertexByIndex(indices[i]), GetVertexByIndex(indices[i + 1]), GetVertexByIndex(indices[i + 2]), detail);
            var vert = new Vector3();
            for (int i = 0; i < vertexBuffer.Count; i += 3)
            {
                vert.set(vertexBuffer[i], vertexBuffer[i + 1], vertexBuffer[i + 2]).normalize().multiplyScalar(radius);
                vertexBuffer[i] = vert.x; vertexBuffer[i + 1] = vert.y; vertexBuffer[i + 2] = vert.z;
            }
            double Azimuth(Vector3 v) => Math.Atan2(v.z, -v.x);
            double Inclination(Vector3 v) => Math.Atan2(-v.y, Math.Sqrt(v.x * v.x + v.z * v.z));
            for (int i = 0; i < vertexBuffer.Count; i += 3)
            {
                vert.set(vertexBuffer[i], vertexBuffer[i + 1], vertexBuffer[i + 2]);
                uvBuffer.Add(Azimuth(vert) / 2 / Math.PI + 0.5);
                uvBuffer.Add(1 - (Inclination(vert) / Math.PI + 0.5));
            }
            // correct seam uvs
            var aa = new Vector3(); var bb2 = new Vector3(); var cc = new Vector3(); var centroid = new Vector3();
            var uvA = new Vector2(); var uvB = new Vector2(); var uvC = new Vector2();
            void Correct(Vector2 uv, int stride, Vector3 vector, double az)
            {
                if (az < 0 && uv.x == 1) uvBuffer[stride] = uv.x - 1;
                if (vector.x == 0 && vector.z == 0) uvBuffer[stride] = az / 2 / Math.PI + 0.5;
            }
            for (int i = 0, j = 0; i < vertexBuffer.Count; i += 9, j += 6)
            {
                aa.set(vertexBuffer[i], vertexBuffer[i + 1], vertexBuffer[i + 2]);
                bb2.set(vertexBuffer[i + 3], vertexBuffer[i + 4], vertexBuffer[i + 5]);
                cc.set(vertexBuffer[i + 6], vertexBuffer[i + 7], vertexBuffer[i + 8]);
                uvA.set(uvBuffer[j], uvBuffer[j + 1]);
                uvB.set(uvBuffer[j + 2], uvBuffer[j + 3]);
                uvC.set(uvBuffer[j + 4], uvBuffer[j + 5]);
                centroid.copy(aa).add(bb2).add(cc).divideScalar(3);
                double az = Azimuth(centroid);
                Correct(uvA, j, aa, az);
                Correct(uvB, j + 2, bb2, az);
                Correct(uvC, j + 4, cc, az);
            }
            for (int i = 0; i < uvBuffer.Count; i += 6)
            {
                double x0 = uvBuffer[i], x1 = uvBuffer[i + 2], x2 = uvBuffer[i + 4];
                double max = Math.Max(x0, Math.Max(x1, x2)), min = Math.Min(x0, Math.Min(x1, x2));
                if (max > 0.9 && min < 0.1)
                {
                    if (x0 < 0.2) uvBuffer[i] += 1;
                    if (x1 < 0.2) uvBuffer[i + 2] += 1;
                    if (x2 < 0.2) uvBuffer[i + 4] += 1;
                }
            }
            setAttribute("position", new Float32BufferAttribute(vertexBuffer, 3));
            setAttribute("normal", new Float32BufferAttribute(new List<double>(vertexBuffer), 3));
            setAttribute("uv", new Float32BufferAttribute(uvBuffer, 2));
            if (detail == 0) computeVertexNormals();
            else normalizeNormals();
        }
    }

    public class TetrahedronGeometry : PolyhedronGeometry
    {
        public TetrahedronGeometry(double radius = 1, int detail = 0)
            : base(new double[] { 1, 1, 1, -1, -1, 1, -1, 1, -1, 1, -1, -1 }, new[] { 2, 1, 0, 0, 3, 2, 1, 3, 0, 2, 3, 1 }, radius, detail) { }
    }

    public class OctahedronGeometry : PolyhedronGeometry
    {
        public OctahedronGeometry(double radius = 1, int detail = 0)
            : base(new double[] { 1, 0, 0, -1, 0, 0, 0, 1, 0, 0, -1, 0, 0, 0, 1, 0, 0, -1 },
                new[] { 0, 2, 4, 0, 4, 3, 0, 3, 5, 0, 5, 2, 1, 2, 5, 1, 5, 3, 1, 3, 4, 1, 4, 2 }, radius, detail) { }
    }

    public class IcosahedronGeometry : PolyhedronGeometry
    {
        private static readonly double T = (1 + Math.Sqrt(5)) / 2;

        public IcosahedronGeometry(double radius = 1, int detail = 0)
            : base(new double[] { -1, T, 0, 1, T, 0, -1, -T, 0, 1, -T, 0, 0, -1, T, 0, 1, T, 0, -1, -T, 0, 1, -T, T, 0, -1, T, 0, 1, -T, 0, -1, -T, 0, 1 },
                new[] { 0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8, 3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1 },
                radius, detail) { }
    }

    public class DodecahedronGeometry : PolyhedronGeometry
    {
        private static readonly double T = (1 + Math.Sqrt(5)) / 2, R = 1 / T;

        public DodecahedronGeometry(double radius = 1, int detail = 0)
            : base(new double[]
                {
                    -1, -1, -1, -1, -1, 1, -1, 1, -1, -1, 1, 1, 1, -1, -1, 1, -1, 1, 1, 1, -1, 1, 1, 1,
                    0, -R, -T, 0, -R, T, 0, R, -T, 0, R, T, -R, -T, 0, -R, T, 0, R, -T, 0, R, T, 0, -T, 0, -R, T, 0, -R, -T, 0, R, T, 0, R,
                },
                new[]
                {
                    3, 11, 7, 3, 7, 15, 3, 15, 13, 7, 19, 17, 7, 17, 6, 7, 6, 15, 17, 4, 8, 17, 8, 10, 17, 10, 6, 8, 0, 16, 8, 16, 2, 8, 2, 10,
                    0, 12, 1, 0, 1, 18, 0, 18, 16, 6, 10, 2, 6, 2, 13, 6, 13, 15, 2, 16, 18, 2, 18, 3, 2, 3, 13, 18, 1, 9, 18, 9, 11, 18, 11, 3,
                    4, 14, 12, 4, 12, 0, 4, 0, 8, 11, 9, 5, 11, 5, 19, 11, 19, 7, 19, 5, 14, 19, 14, 4, 19, 4, 17, 1, 12, 14, 1, 14, 5, 1, 5, 9,
                }, radius, detail) { }
    }

    public class TubeGeometry : BufferGeometry
    {
        public Vector3[] tangents, normalsF, binormals;

        public TubeGeometry(Curve3 path, int tubularSegments = 64, double radius = 1, int radialSegments = 8, bool closed = false)
        {
            type = "TubeGeometry";
            var frames = path.computeFrenetFrames(tubularSegments, closed);
            tangents = frames.tangents;
            normalsF = frames.normals;
            binormals = frames.binormals;
            var b = new GeoBuilder();
            var vertex = new Vector3();
            var normal = new Vector3();
            var P = new Vector3();
            void GenerateSegment(int i)
            {
                path.getPointAt((double)i / tubularSegments, P);
                var N = frames.normals[i];
                var B = frames.binormals[i];
                for (int j = 0; j <= radialSegments; j++)
                {
                    double v = (double)j / radialSegments * Math.PI * 2;
                    double sin = Math.Sin(v), cos = -Math.Cos(v);
                    normal.x = cos * N.x + sin * B.x;
                    normal.y = cos * N.y + sin * B.y;
                    normal.z = cos * N.z + sin * B.z;
                    normal.normalize();
                    b.normals.Add(normal.x); b.normals.Add(normal.y); b.normals.Add(normal.z);
                    vertex.x = P.x + radius * normal.x;
                    vertex.y = P.y + radius * normal.y;
                    vertex.z = P.z + radius * normal.z;
                    b.vertices.Add(vertex.x); b.vertices.Add(vertex.y); b.vertices.Add(vertex.z);
                }
            }
            for (int i = 0; i < tubularSegments; i++) GenerateSegment(i);
            GenerateSegment(closed ? 0 : tubularSegments);
            for (int i = 0; i <= tubularSegments; i++)
            for (int j = 0; j <= radialSegments; j++)
            {
                b.uvs.Add((double)i / tubularSegments);
                b.uvs.Add((double)j / radialSegments);
            }
            for (int j = 1; j <= tubularSegments; j++)
            for (int i = 1; i <= radialSegments; i++)
            {
                int a = (radialSegments + 1) * (j - 1) + (i - 1), bb = (radialSegments + 1) * j + (i - 1);
                int c = (radialSegments + 1) * j + i, d = (radialSegments + 1) * (j - 1) + i;
                b.indices.Add(a); b.indices.Add(bb); b.indices.Add(d);
                b.indices.Add(bb); b.indices.Add(c); b.indices.Add(d);
            }
            b.Apply(this);
        }
    }

    public class ShapeGeometry : BufferGeometry
    {
        public ShapeGeometry(Shape shape, int curveSegments = 12) : this(new[] { shape }, curveSegments) { }

        public ShapeGeometry(IList<Shape> shapes, int curveSegments = 12)
        {
            type = "ShapeGeometry";
            var b = new GeoBuilder();
            int groupStart = 0, groupCount = 0;
            for (int s = 0; s < shapes.Count; s++)
            {
                var shape = shapes[s];
                int indexOffset = b.vertices.Count / 3;
                var points = shape.extractPoints(curveSegments);
                var shapeVertices = points.shape;
                var shapeHoles = points.holes;
                if (!ShapeUtils.isClockWise(shapeVertices)) shapeVertices.Reverse();
                for (int i = 0; i < shapeHoles.Count; i++)
                    if (ShapeUtils.isClockWise(shapeHoles[i])) shapeHoles[i].Reverse();
                var faces = ShapeUtils.triangulateShape(shapeVertices, shapeHoles);
                var all = new List<Vector2>(shapeVertices);
                foreach (var h in shapeHoles) all.AddRange(h);
                foreach (var v in all)
                {
                    b.vertices.Add(v.x); b.vertices.Add(v.y); b.vertices.Add(0);
                    b.normals.Add(0); b.normals.Add(0); b.normals.Add(1);
                    b.uvs.Add(v.x); b.uvs.Add(v.y);
                }
                foreach (var f in faces)
                {
                    b.indices.Add(f[0] + indexOffset); b.indices.Add(f[1] + indexOffset); b.indices.Add(f[2] + indexOffset);
                    groupCount += 3;
                }
                addGroup(groupStart, groupCount, s);
                groupStart += groupCount;
                groupCount = 0;
            }
            b.Apply(this);
        }
    }
}
