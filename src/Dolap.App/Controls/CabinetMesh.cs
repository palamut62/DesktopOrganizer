using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace Dolap.App.Controls;

/// <summary>Small, immutable cabinet meshes. Coordinates are shared by both themes.</summary>
internal static class CabinetMesh
{
    public static MeshGeometry3D Quad(Point3D tl, Point3D bl, Point3D br, Point3D tr)
    {
        var m = new MeshGeometry3D
        {
            Positions = [tl, bl, br, tr], TextureCoordinates = [new(0, 0), new(0, 1), new(1, 1), new(1, 0)],
            TriangleIndices = [0, 1, 2, 0, 2, 3]
        };
        m.Freeze(); return m;
    }

    public static GeometryModel3D Box(double x, double y, double z, double w, double h, double d, Material material, double bevel = 2)
    {
        var mesh = new MeshGeometry3D();
        // Rounded front outline, bevel ring, side walls and back. Separate vertices give crisp normals.
        var radius = Math.Max(bevel, 1);
        bevel = Math.Min(bevel, Math.Min(d * .45, Math.Min(w, h) * .24));
        var outer = Outline(x, y, w, h, radius);
        var inner = Outline(x + bevel, y + bevel, w - 2 * bevel, h - 2 * bevel, Math.Max(.2, radius - bevel));
        var center = new Point3D(x + w / 2, y + h / 2, z + d);
        for (var i = 0; i < outer.Count; i++)
        {
            var j = (i + 1) % outer.Count;
            var a = new Point3D(inner[i].X, inner[i].Y, z + d);
            var b = new Point3D(inner[j].X, inner[j].Y, z + d);
            Triangle(mesh, center, a, b, x, y, w, h);
            var c = new Point3D(outer[i].X, outer[i].Y, z + d - bevel);
            var e = new Point3D(outer[j].X, outer[j].Y, z + d - bevel);
            Triangle(mesh, a, c, e, x, y, w, h); Triangle(mesh, a, e, b, x, y, w, h);
            var f = new Point3D(c.X, c.Y, z); var g = new Point3D(e.X, e.Y, z);
            Triangle(mesh, c, f, g, x, y, w, h); Triangle(mesh, c, g, e, x, y, w, h);
            Triangle(mesh, new(x + w / 2, y + h / 2, z), g, f, x, y, w, h);
        }
        // Project each face onto its own plane. XY mapping collapses side/top UVs
        // to a line and stretches one texture row over the entire panel.
        for (var i = 0; i < mesh.Positions.Count; i++)
        {
            var p = mesh.Positions[i]; var n = mesh.Normals[i];
            mesh.TextureCoordinates[i] = Math.Abs(n.X) > .7
                ? new Point((p.Z - z) / d, 1 - (p.Y - y) / h)
                : Math.Abs(n.Y) > .7
                    ? new Point((p.X - x) / w, (p.Z - z) / d)
                    : new Point((p.X - x) / w, 1 - (p.Y - y) / h);
        }
        mesh.Freeze();
        var model = new GeometryModel3D(mesh, material) { BackMaterial = material };
        model.Freeze(); return model;
    }

    static List<Point> Outline(double x, double y, double w, double h, double radius)
    {
        radius = Math.Min(radius, Math.Min(w, h) / 2);
        var p = new List<Point>();
        foreach (var (cx, cy, start) in new[] { (x + w - radius, y + h - radius, 0), (x + radius, y + h - radius, 90), (x + radius, y + radius, 180), (x + w - radius, y + radius, 270) })
            for (var i = 0; i <= 4; i++)
            {
                var a = (start + i * 22.5) * Math.PI / 180;
                p.Add(new(cx + radius * Math.Cos(a), cy + radius * Math.Sin(a)));
            }
        return p;
    }

    static void Triangle(MeshGeometry3D m, Point3D a, Point3D b, Point3D c, double x, double y, double w, double h)
    {
        var n = m.Positions.Count;
        var normal = Vector3D.CrossProduct(b - a, c - a);
        if (normal.LengthSquared > .000001) normal.Normalize();
        foreach (var p in new[] { a, b, c }) { m.Positions.Add(p); m.Normals.Add(normal); m.TextureCoordinates.Add(new((p.X - x) / w, 1 - (p.Y - y) / h)); }
        m.TriangleIndices.Add(n); m.TriangleIndices.Add(n + 1); m.TriangleIndices.Add(n + 2);
    }

    public static GeometryModel3D Cylinder(Point3D start, Point3D end, double radius, Material material)
    {
        var axis = end - start; axis.Normalize();
        var u = Vector3D.CrossProduct(axis, Math.Abs(axis.Y) < .9 ? new(0, 1, 0) : new Vector3D(1, 0, 0)); u.Normalize();
        var v = Vector3D.CrossProduct(axis, u);
        var mesh = new MeshGeometry3D();
        const int segments = 48;
        for (var i = 0; i < segments; i++)
        {
            var a = i * Math.Tau / segments; var b = (i + 1) * Math.Tau / segments;
            var r1 = radius * (Math.Cos(a) * u + Math.Sin(a) * v);
            var r2 = radius * (Math.Cos(b) * u + Math.Sin(b) * v);
            var sideStart = mesh.Positions.Count;
            Triangle(mesh, start + r1, end + r1, end + r2, 0, 0, 1, 1);
            Triangle(mesh, start + r1, end + r2, start + r2, 0, 0, 1, 1);
            // Smooth only the barrel; end caps retain their crisp axial normal.
            var n1 = r1 / radius; var n2 = r2 / radius;
            var normals = new[] { n1, n1, n2, n1, n2, n2 };
            for (var k = 0; k < normals.Length; k++) mesh.Normals[sideStart + k] = normals[k];
            Triangle(mesh, end, end + r2, end + r1, 0, 0, 1, 1);
            Triangle(mesh, start, start + r1, start + r2, 0, 0, 1, 1);
        }
        mesh.Freeze(); var model = new GeometryModel3D(mesh, material) { BackMaterial = material }; model.Freeze(); return model;
    }

    /// <summary>tint verilirse doku rengi bununla carpilir (gri/beyaz doku + kategori rengi).</summary>
    public static Material Finish(string color, double shine = 50, double reflection = .4, Brush? texture = null, bool tint = false)
    {
        var group = new MaterialGroup();
        var diffuse = new DiffuseMaterial(texture ?? new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)));
        // Ortam isigi da renge carpilmali; yoksa beyaz doku pastel/soluk gorunur.
        if (texture != null && tint) diffuse.Color = diffuse.AmbientColor = (Color)ColorConverter.ConvertFromString(color);
        group.Children.Add(diffuse);
        group.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromScRgb(1, (float)reflection, (float)reflection, (float)reflection)), shine));
        group.Freeze(); return group;
    }

    static Brush? _steel, _plastic;
    /// <summary>tools/gen_assets.py ile uretilen 2048x1024 dokular; tek kez yuklenip paylasilir.</summary>
    public static Brush BrushedSteel() => _steel ??= Texture("tex_brushed_steel.png");
    public static Brush Plastic() => _plastic ??= Texture("tex_abs_plastic.png");

    static Brush Texture(string name)
    {
        var img = new System.Windows.Media.Imaging.BitmapImage(new Uri($"pack://application:,,,/Assets/{name}"));
        img.Freeze();
        var brush = new ImageBrush(img) { Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.HighQuality);
        brush.Freeze(); return brush;
    }
}
