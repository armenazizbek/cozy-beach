using System;
using System.Collections.Generic;

namespace THREE
{
    public class ExtrudeGeometryOptions
    {
        public int curveSegments = 12;
        public int steps = 1;
        public double depth = 1;
        public bool bevelEnabled = true;
        public double bevelThickness = 0.2;
        /// <summary>Null means bevelThickness - 0.1, as in Three.</summary>
        public double? bevelSize;
        public double bevelOffset = 0;
        public int bevelSegments = 3;
    }

    /// <summary>Three.js r170's ExtrudeGeometry (straight extrusion along z, with bevels).</summary>
    public class ExtrudeGeometry : BufferGeometry
    {
        public ExtrudeGeometry(Shape shape, ExtrudeGeometryOptions options = null) : this(new[] { shape }, options) { }

        public ExtrudeGeometry(IList<Shape> shapes, ExtrudeGeometryOptions options = null)
        {
            type = "ExtrudeGeometry";
            options ??= new ExtrudeGeometryOptions();
            var verticesArray = new List<double>();
            var uvArray = new List<double>();
            foreach (var shape in shapes) AddShape(shape, options, verticesArray, uvArray);
            setAttribute("position", new Float32BufferAttribute(verticesArray, 3));
            setAttribute("uv", new Float32BufferAttribute(uvArray, 2));
            computeVertexNormals();
        }

        private static Vector2 ScalePt2(Vector2 pt, Vector2 vec, double size) => pt.clone().add(vec.clone().multiplyScalar(size));

        private static void MergeOverlappingPoints(List<Vector2> points)
        {
            const double THRESHOLD = 1e-10, THRESHOLD_SQ = THRESHOLD * THRESHOLD;
            if (points.Count == 0) return;
            var prevPos = points[0];
            for (int i = 1; i <= points.Count; i++)
            {
                int currentIndex = i % points.Count;
                var currentPos = points[currentIndex];
                double dx = currentPos.x - prevPos.x, dy = currentPos.y - prevPos.y, distSq = dx * dx + dy * dy;
                double s = JS.Max(Math.Abs(currentPos.x), Math.Abs(currentPos.y), Math.Abs(prevPos.x), Math.Abs(prevPos.y));
                if (distSq <= THRESHOLD_SQ * s * s)
                {
                    points.RemoveAt(currentIndex);
                    i--;
                    continue;
                }
                prevPos = currentPos;
            }
        }

        private static Vector2 GetBevelVec(Vector2 inPt, Vector2 inPrev, Vector2 inNext)
        {
            double v_trans_x, v_trans_y, shrink_by;
            double v_prev_x = inPt.x - inPrev.x, v_prev_y = inPt.y - inPrev.y;
            double v_next_x = inNext.x - inPt.x, v_next_y = inNext.y - inPt.y;
            double v_prev_lensq = v_prev_x * v_prev_x + v_prev_y * v_prev_y;
            double collinear0 = v_prev_x * v_next_y - v_prev_y * v_next_x;
            if (Math.Abs(collinear0) > Interp.EPS)
            {
                double v_prev_len = Math.Sqrt(v_prev_lensq);
                double v_next_len = Math.Sqrt(v_next_x * v_next_x + v_next_y * v_next_y);
                double ptPrevShift_x = inPrev.x - v_prev_y / v_prev_len, ptPrevShift_y = inPrev.y + v_prev_x / v_prev_len;
                double ptNextShift_x = inNext.x - v_next_y / v_next_len, ptNextShift_y = inNext.y + v_next_x / v_next_len;
                double sf = ((ptNextShift_x - ptPrevShift_x) * v_next_y - (ptNextShift_y - ptPrevShift_y) * v_next_x) / (v_prev_x * v_next_y - v_prev_y * v_next_x);
                v_trans_x = ptPrevShift_x + v_prev_x * sf - inPt.x;
                v_trans_y = ptPrevShift_y + v_prev_y * sf - inPt.y;
                double v_trans_lensq = v_trans_x * v_trans_x + v_trans_y * v_trans_y;
                if (v_trans_lensq <= 2) return new Vector2(v_trans_x, v_trans_y);
                shrink_by = Math.Sqrt(v_trans_lensq / 2);
            }
            else
            {
                bool direction_eq = false;
                if (v_prev_x > Interp.EPS) { if (v_next_x > Interp.EPS) direction_eq = true; }
                else if (v_prev_x < -Interp.EPS) { if (v_next_x < -Interp.EPS) direction_eq = true; }
                else if (JS.Sign(v_prev_y) == JS.Sign(v_next_y)) direction_eq = true;
                if (direction_eq)
                {
                    v_trans_x = -v_prev_y;
                    v_trans_y = v_prev_x;
                    shrink_by = Math.Sqrt(v_prev_lensq);
                }
                else
                {
                    v_trans_x = v_prev_x;
                    v_trans_y = v_prev_y;
                    shrink_by = Math.Sqrt(v_prev_lensq / 2);
                }
            }
            return new Vector2(v_trans_x / shrink_by, v_trans_y / shrink_by);
        }

        private void AddShape(Shape shape, ExtrudeGeometryOptions o, List<double> verticesArray, List<double> uvArray)
        {
            var placeholder = new List<double>();
            int curveSegments = o.curveSegments, steps = o.steps;
            double depth = o.depth;
            bool bevelEnabled = o.bevelEnabled;
            double bevelThickness = o.bevelThickness;
            double bevelSize = o.bevelSize ?? bevelThickness - 0.1;
            double bevelOffset = o.bevelOffset;
            int bevelSegments = o.bevelSegments;
            if (!bevelEnabled)
            {
                bevelSegments = 0;
                bevelThickness = 0;
                bevelSize = 0;
                bevelOffset = 0;
            }
            var shapePoints = shape.extractPoints(curveSegments);
            var vertices = shapePoints.shape;
            var holes = shapePoints.holes;
            bool reverse = !ShapeUtils.isClockWise(vertices);
            if (reverse)
            {
                vertices.Reverse();
                for (int h = 0; h < holes.Count; h++)
                    if (ShapeUtils.isClockWise(holes[h])) holes[h].Reverse();
            }
            MergeOverlappingPoints(vertices);
            foreach (var h in holes) MergeOverlappingPoints(h);
            int numHoles = holes.Count;
            var contour = vertices;
            var allVertices = new List<Vector2>(vertices);
            foreach (var h in holes) allVertices.AddRange(h);
            int vlen = allVertices.Count;

            void V(double x, double y, double z) { placeholder.Add(x); placeholder.Add(y); placeholder.Add(z); }

            var contourMovements = new List<Vector2>();
            for (int i = 0, il = contour.Count, j = il - 1, k = i + 1; i < il; i++, j++, k++)
            {
                if (j == il) j = 0;
                if (k == il) k = 0;
                contourMovements.Add(GetBevelVec(contour[i], contour[j], contour[k]));
            }
            var holesMovements = new List<List<Vector2>>();
            var verticesMovements = new List<Vector2>(contourMovements);
            foreach (var ahole in holes)
            {
                var one = new List<Vector2>();
                for (int i = 0, il = ahole.Count, j = il - 1, k = i + 1; i < il; i++, j++, k++)
                {
                    if (j == il) j = 0;
                    if (k == il) k = 0;
                    one.Add(GetBevelVec(ahole[i], ahole[j], ahole[k]));
                }
                holesMovements.Add(one);
                verticesMovements.AddRange(one);
            }
            List<int[]> faces;
            if (bevelSegments == 0) faces = ShapeUtils.triangulateShape(contour, holes);
            else
            {
                var contracted = new List<Vector2>();
                var expandedHoles = new List<List<Vector2>>();
                for (int b = 0; b < bevelSegments; b++)
                {
                    double t = (double)b / bevelSegments;
                    double z = bevelThickness * Math.Cos(t * Math.PI / 2);
                    double bs1 = bevelSize * Math.Sin(t * Math.PI / 2) + bevelOffset;
                    for (int i = 0; i < contour.Count; i++)
                    {
                        var vert = ScalePt2(contour[i], contourMovements[i], bs1);
                        V(vert.x, vert.y, -z);
                        if (t == 0) contracted.Add(vert);
                    }
                    for (int h = 0; h < numHoles; h++)
                    {
                        var ahole = holes[h];
                        var one = holesMovements[h];
                        var oneVerts = new List<Vector2>();
                        for (int i = 0; i < ahole.Count; i++)
                        {
                            var vert = ScalePt2(ahole[i], one[i], bs1);
                            V(vert.x, vert.y, -z);
                            if (t == 0) oneVerts.Add(vert);
                        }
                        if (t == 0) expandedHoles.Add(oneVerts);
                    }
                }
                faces = ShapeUtils.triangulateShape(contracted, expandedHoles);
            }
            int flen = faces.Count;
            double bs = bevelSize + bevelOffset;
            for (int i = 0; i < vlen; i++)
            {
                var vert = bevelEnabled ? ScalePt2(allVertices[i], verticesMovements[i], bs) : allVertices[i];
                V(vert.x, vert.y, 0);
            }
            for (int s = 1; s <= steps; s++)
            for (int i = 0; i < vlen; i++)
            {
                var vert = bevelEnabled ? ScalePt2(allVertices[i], verticesMovements[i], bs) : allVertices[i];
                V(vert.x, vert.y, depth / steps * s);
            }
            for (int b = bevelSegments - 1; b >= 0; b--)
            {
                double t = (double)b / bevelSegments;
                double z = bevelThickness * Math.Cos(t * Math.PI / 2);
                double bs2 = bevelSize * Math.Sin(t * Math.PI / 2) + bevelOffset;
                for (int i = 0; i < contour.Count; i++)
                {
                    var vert = ScalePt2(contour[i], contourMovements[i], bs2);
                    V(vert.x, vert.y, depth + z);
                }
                for (int h = 0; h < holes.Count; h++)
                {
                    var ahole = holes[h];
                    var one = holesMovements[h];
                    for (int i = 0; i < ahole.Count; i++)
                    {
                        var vert = ScalePt2(ahole[i], one[i], bs2);
                        V(vert.x, vert.y, depth + z);
                    }
                }
            }

            void AddVertex(int index)
            {
                verticesArray.Add(placeholder[index * 3]);
                verticesArray.Add(placeholder[index * 3 + 1]);
                verticesArray.Add(placeholder[index * 3 + 2]);
            }
            void AddUV(double x, double y) { uvArray.Add(x); uvArray.Add(y); }
            void F3(int a, int b, int c)
            {
                AddVertex(a); AddVertex(b); AddVertex(c);
                int n = verticesArray.Count / 3;
                for (int k = 3; k >= 1; k--) AddUV(verticesArray[(n - k) * 3], verticesArray[(n - k) * 3 + 1]);
            }
            void F4(int a, int b, int c, int d)
            {
                AddVertex(a); AddVertex(b); AddVertex(d);
                AddVertex(b); AddVertex(c); AddVertex(d);
                int n = verticesArray.Count / 3;
                int ia = n - 6, ib = n - 3, ic = n - 2, id = n - 1;
                double ax = verticesArray[ia * 3], ay = verticesArray[ia * 3 + 1], az = verticesArray[ia * 3 + 2];
                double bx = verticesArray[ib * 3], by = verticesArray[ib * 3 + 1], bz = verticesArray[ib * 3 + 2];
                double cx = verticesArray[ic * 3], cy = verticesArray[ic * 3 + 1], cz = verticesArray[ic * 3 + 2];
                double dx = verticesArray[id * 3], dy = verticesArray[id * 3 + 1], dz = verticesArray[id * 3 + 2];
                Vector2 ua, ub, uc, ud;
                if (Math.Abs(ay - by) < Math.Abs(ax - bx))
                {
                    ua = new Vector2(ax, 1 - az); ub = new Vector2(bx, 1 - bz); uc = new Vector2(cx, 1 - cz); ud = new Vector2(dx, 1 - dz);
                }
                else
                {
                    ua = new Vector2(ay, 1 - az); ub = new Vector2(by, 1 - bz); uc = new Vector2(cy, 1 - cz); ud = new Vector2(dy, 1 - dz);
                }
                AddUV(ua.x, ua.y); AddUV(ub.x, ub.y); AddUV(ud.x, ud.y);
                AddUV(ub.x, ub.y); AddUV(uc.x, uc.y); AddUV(ud.x, ud.y);
            }

            // lids
            {
                int start = verticesArray.Count / 3;
                if (bevelEnabled)
                {
                    int offset = 0;
                    foreach (var f in faces) F3(f[2] + offset, f[1] + offset, f[0] + offset);
                    offset = vlen * (steps + bevelSegments * 2);
                    foreach (var f in faces) F3(f[0] + offset, f[1] + offset, f[2] + offset);
                }
                else
                {
                    foreach (var f in faces) F3(f[2], f[1], f[0]);
                    foreach (var f in faces) F3(f[0] + vlen * steps, f[1] + vlen * steps, f[2] + vlen * steps);
                }
                addGroup(start, verticesArray.Count / 3 - start, 0);
            }
            // sides
            {
                int start = verticesArray.Count / 3;
                int layeroffset = 0;
                void Sidewalls(List<Vector2> c, int lo)
                {
                    int i = c.Count;
                    while (--i >= 0)
                    {
                        int j = i, k = i - 1;
                        if (k < 0) k = c.Count - 1;
                        for (int s = 0, sl = steps + bevelSegments * 2; s < sl; s++)
                        {
                            int slen1 = vlen * s, slen2 = vlen * (s + 1);
                            F4(lo + j + slen1, lo + k + slen1, lo + k + slen2, lo + j + slen2);
                        }
                    }
                }
                Sidewalls(contour, layeroffset);
                layeroffset += contour.Count;
                foreach (var h in holes)
                {
                    Sidewalls(h, layeroffset);
                    layeroffset += h.Count;
                }
                addGroup(start, verticesArray.Count / 3 - start, 1);
            }
            _ = flen;
        }
    }
}
