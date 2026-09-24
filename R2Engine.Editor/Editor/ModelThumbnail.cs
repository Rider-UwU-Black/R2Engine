using System.Numerics;
using R2Engine.Editor.Scene;
using StbImageSharp;

namespace R2Engine.Editor;

// Small CPU preview renderer: never touches the scene, animation pose, or GL state.
internal static class ModelThumbnail
{
    public static byte[] Render(string path, int size)
    {
        Material material = new();
        Mesh mesh;
        switch (Path.GetExtension(path).ToLowerInvariant())
        {
            case ".r2mat":
                material = MaterialSerializer.Load(path);
                mesh = PrimitiveMeshes.Sphere;
                break;
            case ".obj": mesh = ObjImporter.Load(path); break;
            default:
                var asset = SkeletalAsset.Load(path);
                var data = new float[asset.Vertices.Count * 8];
                for (int i = 0; i < asset.Vertices.Count; i++)
                {
                    var v = asset.Vertices[i]; int o = i * 8;
                    data[o] = v.Position.X; data[o+1] = v.Position.Y; data[o+2] = v.Position.Z;
                    data[o+3] = v.Normal.X; data[o+4] = v.Normal.Y; data[o+5] = v.Normal.Z;
                    data[o+6] = v.UV.X; data[o+7] = v.UV.Y;
                }
                mesh = new Mesh(asset.Name, data, asset.Indices.ToArray());
                break;
        }
        ImageResult? image = null;
        if (!string.IsNullOrWhiteSpace(material.TexturePath))
        {
            string texturePath = AssetDatabase.ToAbsolutePath(material.TexturePath);
            if (File.Exists(texturePath))
                image = ImageResult.FromMemory(File.ReadAllBytes(texturePath), ColorComponents.RedGreenBlueAlpha);
        }
        var rotation = Matrix4x4.CreateRotationY(-0.55f) * Matrix4x4.CreateRotationX(0.25f);
        var positions = new Vector3[mesh.VertexCount];
        var normals = new Vector3[mesh.VertexCount];
        Vector3 min = new(float.PositiveInfinity), max = new(float.NegativeInfinity);
        for (int i = 0; i < positions.Length; i++)
        {
            int o = i * 8; var d = mesh.VertexData;
            positions[i] = Vector3.Transform(new Vector3(d[o], d[o+1], d[o+2]) - mesh.BoundsCenter, rotation);
            normals[i] = Vector3.TransformNormal(new Vector3(d[o+3], d[o+4], d[o+5]), rotation);
            min = Vector3.Min(min, positions[i]); max = Vector3.Max(max, positions[i]);
        }
        float scale = (size - 12) / Math.Max(0.0001f, Math.Max(max.X-min.X, max.Y-min.Y));
        var center = (min + max) * 0.5f;
        for (int i = 0; i < positions.Length; i++)
            positions[i] = new Vector3((positions[i].X-center.X)*scale+size/2f,
                size/2f-(positions[i].Y-center.Y)*scale, -positions[i].Z);
        byte[] pixels = new byte[size * size * 4];
        float[] depth = Enumerable.Repeat(float.PositiveInfinity, size * size).ToArray();
        var light = Vector3.Normalize(new Vector3(-0.4f, 0.7f, 1));
        for (int t = 0; t + 2 < mesh.Indices.Length; t += 3)
        {
            int a = (int)mesh.Indices[t], b = (int)mesh.Indices[t+1], c = (int)mesh.Indices[t+2];
            var p = positions[a]; var q = positions[b]; var r = positions[c];
            float area = Edge(p,q,r.X,r.Y);
            if (!float.IsFinite(area) || Math.Abs(area) < 0.001f) continue;
            int left = Math.Clamp((int)MathF.Floor(Math.Min(p.X,Math.Min(q.X,r.X))),0,size-1);
            int right = Math.Clamp((int)MathF.Ceiling(Math.Max(p.X,Math.Max(q.X,r.X))),0,size-1);
            int top = Math.Clamp((int)MathF.Floor(Math.Min(p.Y,Math.Min(q.Y,r.Y))),0,size-1);
            int bottom = Math.Clamp((int)MathF.Ceiling(Math.Max(p.Y,Math.Max(q.Y,r.Y))),0,size-1);
            for (int y = top; y <= bottom; y++) for (int x = left; x <= right; x++)
            {
                float u = Edge(q,r,x+0.5f,y+0.5f)/area, v = Edge(r,p,x+0.5f,y+0.5f)/area, w = 1-u-v;
                if (u < 0 || v < 0 || w < 0) continue;
                int index = y*size+x; float z = u*p.Z+v*q.Z+w*r.Z;
                if (z >= depth[index]) continue;
                Vector4 color = material.BaseColor;
                if (image != null)
                {
                    float tx = u*mesh.VertexData[a*8+6]+v*mesh.VertexData[b*8+6]+w*mesh.VertexData[c*8+6];
                    float ty = u*mesh.VertexData[a*8+7]+v*mesh.VertexData[b*8+7]+w*mesh.VertexData[c*8+7];
                    int ix = Math.Clamp((int)((tx-MathF.Floor(tx))*image.Width),0,image.Width-1);
                    int iy = Math.Clamp((int)((1-(ty-MathF.Floor(ty)))*image.Height),0,image.Height-1);
                    int o = (iy*image.Width+ix)*4;
                    color *= new Vector4(image.Data[o],image.Data[o+1],image.Data[o+2],image.Data[o+3])/255f;
                }
                if (material.SurfaceMode == MaterialSurfaceMode.Cutout && color.W < material.AlphaCutoff) continue;
                var n = u*normals[a]+v*normals[b]+w*normals[c];
                float lighting = material.LightingMode == MaterialLightingMode.Unlit ? 1 :
                    0.3f + 0.7f*Math.Max(0, Vector3.Dot(n.LengthSquared()>0 ? Vector3.Normalize(n) : Vector3.UnitZ,light));
                depth[index] = z;
                pixels[index*4] = (byte)Math.Clamp(color.X*lighting*255,0,255);
                pixels[index*4+1] = (byte)Math.Clamp(color.Y*lighting*255,0,255);
                pixels[index*4+2] = (byte)Math.Clamp(color.Z*lighting*255,0,255);
                pixels[index*4+3] = material.SurfaceMode == MaterialSurfaceMode.Transparent ? (byte)Math.Clamp(color.W*255,0,255) : (byte)255;
            }
        }
        return pixels;
    }

    private static float Edge(Vector3 a, Vector3 b, float x, float y) => (b.X-a.X)*(y-a.Y)-(b.Y-a.Y)*(x-a.X);
}
