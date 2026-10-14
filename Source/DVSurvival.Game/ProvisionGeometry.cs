using System;
using System.Collections.Generic;
using DVSurvival.Core;

namespace DVSurvival.Mod
{
    // Pure mesh data: testable without a Unity player; metres, front faces towards -Z.
    internal sealed class ProvisionGeometry
    {
        internal struct Point
        {
            public float X, Y, Z;
            public Point(float x, float y, float z) { X = x; Y = y; Z = z; }
        }
        internal struct UV
        {
            public float U, V;
            public UV(float u, float v) { U = u; V = v; }
        }
        public readonly List<Point> Vertices = new List<Point>();
        public readonly List<Point> Normals = new List<Point>();
        public readonly List<UV> TexCoords = new List<UV>();
        public readonly List<int> Triangles = new List<int>();
        public int LabelStart;
        private const int Segments = 24;

        public static ProvisionGeometry Create(ProvisionKind kind)
        {
            var g = new ProvisionGeometry();
            switch (kind)
            {
                case ProvisionKind.Meal:
                    // A robust resealable field-ration pouch, with a gusseted bottom and folded seal.
                    g.Pouch(0, .086f, .13f, .15f, .035f, 0);
                    g.Slab(0, .015f, 0, .125f, .012f, .021f, .004f, 1);
                    g.Slab(0, .164f, 0, .13f, .01f, .008f, .003f, 1);
                    g.Box(0, .153f, -.009f, .119f, .0015f, .0015f, 9);
                    for (int i = 0; i < 16; i++)
                        g.Box(-.056f + i * .0075f, .164f, -.0045f, .0007f, .007f, .0005f, 0);
                    break;
                case ProvisionKind.Water:
                    // Screw-top travel bottle: short neck, grip ribs and reinforced base.
                    g.Lathe(2, 0,.028f, .004f,.032f, .01f,.0335f, .016f,.034f, .128f,.034f,
                        .139f,.032f, .153f,.026f, .165f,.019f, .184f,.018f);
                    g.Band(9, .001f,.029f, .006f,.035f, .012f,.034f);
                    foreach (float y in new[] { .026f, .033f, .118f, .125f })
                        g.Band(2, y,.034f, y+.002f,.035f, y+.004f,.034f);
                    g.Lathe(3, .181f,.018f, .184f,.021f, .188f,.022f, .199f,.022f, .204f,.02f, .205f,.018f);
                    g.Band(9, .178f,.0185f, .182f,.0185f);
                    break;
                case ProvisionKind.Coffee:
                    // Compact steel vacuum flask, screw lid/cup and protective rubber bands.
                    g.Lathe(5, 0,.027f, .003f,.031f, .008f,.033f, .017f,.033f, .15f,.033f,
                        .158f,.031f, .17f,.023f, .179f,.022f);
                    g.Band(9, .001f,.028f, .005f,.034f, .016f,.034f, .018f,.033f);
                    g.Band(6, .038f,.033f, .041f,.0335f, .127f,.0335f, .13f,.033f);
                    g.Lathe(9, .17f,.025f, .174f,.031f, .178f,.032f, .202f,.032f, .208f,.03f, .211f,.027f);
                    g.Band(5, .176f,.0325f, .18f,.0325f);
                    break;
                case ProvisionKind.FirstAid:
                    // Compact moulded medical case, sealed clamshell and recessed carry handle.
                    g.RoundedBox(0,.064f,0,.164f,.12f,.05f,.012f,.004f,7);
                    g.RoundedBox(0,.064f,0,.165f,.121f,.002f,.012f,.0005f,9);
                    foreach(float x in new[] { -.052f,.052f })
                    {
                        // Each clasp bridges the lid/body seam at Z=0 on the upper end.
                        // Nothing is attached to the printed front face.
                        g.EndRoundedBox(x,.1255f,0,.018f,.03f,.006f,.003f,.001f,9);
                        g.Box(x,.1286f,.009f,.012f,.0008f,.002f,5);
                        g.CaseHinge(x);
                    }
                    // Handle mounts belong only to the rear half, leaving the opening seam free.
                    g.RoundedBox(-.032f,.124f,.012f,.014f,.008f,.016f,.002f,.001f,9);
                    g.RoundedBox(.032f,.124f,.012f,.014f,.008f,.016f,.002f,.001f,9);
                    g.CarryStrap(.124f,.032f,.015f,.008f,.012f,.012f);
                    break;
                case ProvisionKind.HeatPack:
                    // Flat pocket-sized dry warmer with foil seams and a tear corner.
                    g.Pouch(0,.073f,.096f,.123f,.011f,8);
                    g.Slab(0,.013f,0,.096f,.008f,.004f,.002f,3);
                    g.Slab(0,.133f,0,.096f,.008f,.004f,.002f,3);
                    for (int i = 0; i < 14; i++)
                        g.Box(-.042f+i*.0064f,.133f,-.0024f,.0006f,.006f,.0005f,8);
                    break;
                case ProvisionKind.CannedFood:
                    // Low tin can: rolled seams, stamped lid and pull tab.
                    g.Lathe(5, 0,.039f, .004f,.046f, .009f,.046f, .1f,.046f, .105f,.039f);
                    g.Band(4, .012f,.0465f, .096f,.0465f);
                    g.Band(5, .002f,.043f, .005f,.048f, .009f,.048f, .011f,.046f);
                    g.Band(5, .096f,.046f, .099f,.048f, .105f,.048f, .108f,.043f);
                    g.Lathe(5, .105f,.043f, .106f,.039f, .109f,.036f);
                    g.Band(4, .109f,.026f, .1095f,.028f, .109f,.03f);
                    g.PullTab(.111f);
                    break;
                case ProvisionKind.Tea:
                    // Insulated trail flask with a canvas sleeve, lid/cup and a carry handle.
                    g.Lathe(5, 0,.027f, .005f,.032f, .022f,.032f, .161f,.032f, .176f,.024f);
                    g.Band(1, .019f,.032f, .022f,.033f, .152f,.033f, .155f,.032f);
                    g.Band(9, .004f,.032f, .007f,.033f, .014f,.033f, .017f,.032f);
                    g.Band(5, .157f,.033f, .166f,.03f);
                    g.Lathe(1, .168f,.027f, .176f,.033f, .18f,.034f, .204f,.034f, .21f,.032f, .213f,.028f);
                    g.SideHandle(.086f, .033f, .011f, .043f, .004f);
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
            g.LabelStart = g.Triangles.Count;
            int label = ProvisionCatalog.ArtworkIndex(kind);
            if (kind == ProvisionKind.Water) g.CurvedLabel(label,.035f,.042f,.108f);
            else if (kind == ProvisionKind.Coffee) g.CurvedLabel(label,.0345f,.046f,.122f);
            else if (kind == ProvisionKind.CannedFood) g.CurvedLabel(label,.0475f,.018f,.09f);
            else if (kind == ProvisionKind.Tea) g.CurvedLabel(label,.034f,.043f,.136f);
            else if (kind == ProvisionKind.Meal) g.PouchLabel(label,.098f,.102f,.086f,.13f,.15f,.035f);
            else if (kind == ProvisionKind.FirstAid) g.Label(label,.084f,.082f,.068f,-.0252f);
            else g.PouchLabel(label,.074f,.094f,.073f,.096f,.123f,.011f);
            return g;
        }

        private void PullTab(float y)
        {
            // An oval ring on the lid; all geometry shares the packaging mesh and collider.
            for (int segment = 0; segment < 16; segment++)
            {
                double a = segment * Math.PI * 2 / 16, b = (segment + 1) * Math.PI * 2 / 16;
                var ao = new Point((float)Math.Sin(a) * .011f, y, (float)Math.Cos(a) * .017f);
                var bo = new Point((float)Math.Sin(b) * .011f, y, (float)Math.Cos(b) * .017f);
                var ai = new Point((float)Math.Sin(a) * .0065f, y, (float)Math.Cos(a) * .011f);
                var bi = new Point((float)Math.Sin(b) * .0065f, y, (float)Math.Cos(b) * .011f);
                var al = new Point(ao.X,y-.0015f,ao.Z); var bl = new Point(bo.X,y-.0015f,bo.Z);
                var ail = new Point(ai.X,y-.0015f,ai.Z); var bil = new Point(bi.X,y-.0015f,bi.Z);
                Quad(ai,ao,bo,bi,4);
                Quad(ail,bil,bl,al,4);
                Quad(ao,al,bl,bo,5);
                Quad(ai,bi,bil,ail,5);
            }
            Slab(0,y-.0008f,-.017f,.008f,.003f,.01f,.001f,4);
            Box(0,y+.0008f,-.017f,.0035f,.001f,.0035f,5);
        }

        private static Point Unit(Point point)
        {
            float length = (float)Math.Sqrt(point.X * point.X + point.Y * point.Y + point.Z * point.Z);
            return length > .0000001f ? new Point(point.X / length, point.Y / length, point.Z / length) : new Point(0, 1, 0);
        }
        private static Point FaceNormal(Point a, Point b, Point c)
        {
            float x1 = b.X - a.X, y1 = b.Y - a.Y, z1 = b.Z - a.Z;
            float x2 = c.X - a.X, y2 = c.Y - a.Y, z2 = c.Z - a.Z;
            return Unit(new Point(y1 * z2 - z1 * y2, z1 * x2 - x1 * z2, x1 * y2 - y1 * x2));
        }
        private UV SurfaceUV(int color, Point p, Point normal)
        {
            float x = Math.Abs(normal.X) > Math.Abs(normal.Z) ? p.Z : p.X;
            float y = Math.Abs(normal.Y) > .75f ? p.Z : p.Y - .1f;
            float u = Math.Max(.05f, Math.Min(.95f, .5f + x / .2f));
            float v = Math.Max(.05f, Math.Min(.95f, .5f + y / .25f));
            return new UV((color * 128 + 4 + u * 120) / 2048f, (188 - (1 - v) * 120) / 512f);
        }
        private UV LabelUV(int label, float u, float v)
        {
            // Inset half a texel; 12px artwork border prevents mip bleed.
            return new UV((label * 256 + .5f + u * 255) / 2048f, (256.5f + v * 255) / 512f);
        }
        private void Quad(Point a, Point b, Point c, Point d, int color)
        {
            var normal = FaceNormal(a, b, c);
            Quad(a,b,c,d,SurfaceUV(color,a,normal),SurfaceUV(color,b,normal),SurfaceUV(color,c,normal),SurfaceUV(color,d,normal));
        }
        private void Quad(Point a, Point b, Point c, Point d, UV ua, UV ub, UV uc, UV ud)
        {
            int start = Vertices.Count;
            var normal = FaceNormal(a, b, c);
            Vertices.AddRange(new[] { a,b,c,d });
            Normals.AddRange(new[] { normal,normal,normal,normal });
            TexCoords.AddRange(new[] { ua,ub,uc,ud });
            Triangles.AddRange(new[] { start,start+1,start+2 });
            if (a.X != d.X || a.Y != d.Y || a.Z != d.Z)
                Triangles.AddRange(new[] { start,start+2,start+3 });
        }
        private void Box(float x,float y,float z,float w,float h,float depth,int color)
        {
            var a=new Point(x-w/2,y-h/2,z-depth/2); var b=new Point(x+w/2,y-h/2,z-depth/2);
            var c=new Point(x+w/2,y+h/2,z-depth/2); var d=new Point(x-w/2,y+h/2,z-depth/2);
            var e=new Point(a.X,a.Y,z+depth/2); var f=new Point(b.X,b.Y,e.Z);
            var g=new Point(c.X,c.Y,e.Z); var h0=new Point(d.X,d.Y,e.Z);
            Quad(a,d,c,b,color); Quad(e,f,g,h0,color);
            Quad(a,e,h0,d,color); Quad(b,c,g,f,color);
            Quad(d,h0,g,c,color); Quad(a,b,f,e,color);
        }
        private void Slab(float x,float y,float z,float w,float h,float depth,float bevel,int color)
        {
            var outline = new[] {
                new Point(-w/2+bevel,-h/2,0),new Point(w/2-bevel,-h/2,0),
                new Point(w/2,-h/2+bevel,0),new Point(w/2,h/2-bevel,0),
                new Point(w/2-bevel,h/2,0),new Point(-w/2+bevel,h/2,0),
                new Point(-w/2,h/2-bevel,0),new Point(-w/2,-h/2+bevel,0) };
            // A shallow bevel on both sides catches light along the packaging edges.
            for (int i=0;i<8;i++)
            {
                var p=outline[i]; var q=outline[(i+1)%8];
                var a=new Point(x+p.X,y+p.Y,z-depth*.32f); var b=new Point(x+q.X,y+q.Y,a.Z);
                var c=new Point(b.X,b.Y,z+depth*.32f); var d=new Point(a.X,a.Y,c.Z);
                var af=new Point(x+p.X*.93f,y+p.Y*.93f,z-depth/2);
                var bf=new Point(x+q.X*.93f,y+q.Y*.93f,af.Z);
                var ab=new Point(af.X,af.Y,z+depth/2); var bb=new Point(bf.X,bf.Y,ab.Z);
                Quad(a,b,c,d,color); Quad(af,bf,b,a,color); Quad(d,c,bb,ab,color);
                // Triangle fans (fourth vertex duplicates center).
                Quad(new Point(x,y,af.Z),bf,af,new Point(x,y,af.Z),color);
                Quad(new Point(x,y,ab.Z),ab,bb,new Point(x,y,ab.Z),color);
            }
        }
        private void RoundedBox(float x,float y,float z,float width,float height,float depth,float radius,float bevel,int color)
        {
            const int cornerSteps=4, count=4*(cornerSteps+1);
            var rings=new Point[4,count]; var radial=new Point[count];
            var inset=new[] { bevel,0f,0f,bevel };
            var zs=new[] { -depth*.5f,-depth*.5f+bevel,depth*.5f-bevel,depth*.5f };
            for(int ring=0;ring<4;ring++) for(int corner=0;corner<4;corner++) for(int step=0;step<=cornerSteps;step++)
            {
                double angle=(corner+step/(double)cornerSteps)*Math.PI*.5;
                float nx=(float)Math.Cos(angle),ny=(float)Math.Sin(angle);
                int index=corner*(cornerSteps+1)+step;
                float cx=(corner==0||corner==3?1:-1)*(width*.5f-radius);
                float cy=(corner<2?1:-1)*(height*.5f-radius);
                float r=radius-inset[ring];
                rings[ring,index]=new Point(x+cx+nx*r,y+cy+ny*r,z+zs[ring]);
                radial[index]=new Point(nx,ny,0);
            }
            for(int ring=0;ring<3;ring++) for(int i=0;i<count;i++)
            {
                int j=(i+1)%count,start=Vertices.Count;
                Quad(rings[ring,i],rings[ring,j],rings[ring+1,j],rings[ring+1,i],color);
                float nz=ring==0?-1: ring==2?1:0;
                Normals[start]=Unit(new Point(radial[i].X,radial[i].Y,nz));
                Normals[start+1]=Unit(new Point(radial[j].X,radial[j].Y,nz));
                Normals[start+2]=Normals[start+1]; Normals[start+3]=Normals[start];
            }
            for(int i=0;i<count;i++)
            {
                int j=(i+1)%count;
                var front=new Point(x,y,z+zs[0]); var back=new Point(x,y,z+zs[3]);
                Quad(front,rings[0,j],rings[0,i],front,color);
                Quad(back,rings[3,i],rings[3,j],back,color);
            }
        }
        private void EndRoundedBox(float x,float y,float z,float width,float span,float thickness,float radius,float bevel,int color)
        {
            int start=Vertices.Count;
            RoundedBox(x,z,0,width,span,thickness,radius,bevel,color);
            // Rotate the local XY outline onto the XZ end face; preserve winding and normals.
            for(int i=start;i<Vertices.Count;i++)
            {
                var p=Vertices[i]; var n=Normals[i];
                Vertices[i]=new Point(p.X,y-p.Z,p.Y);
                Normals[i]=new Point(n.X,-n.Z,n.Y);
            }
        }
        private void CaseHinge(float x)
        {
            // Mounting leaves on both halves at the opposite end from the clasps.
            Box(x,.004f,-.0065f,.022f,.005f,.009f,9);
            Box(x,.004f,.0065f,.022f,.005f,.009f,9);
            int start=Vertices.Count;
            Lathe(5,0,.0025f,.022f,.0025f);
            for(int i=start;i<Vertices.Count;i++)
            {
                var p=Vertices[i]; var n=Normals[i];
                Vertices[i]=new Point(x+p.Y-.011f,.0027f-p.X,p.Z);
                Normals[i]=new Point(n.Y,-n.X,n.Z);
            }
        }
        private static Point Ring(float y,float radius,double angle)
        { return new Point((float)Math.Sin(angle)*radius,y,-(float)Math.Cos(angle)*radius); }
        private void Lathe(int color, params float[] profile)
        { LatheSurface(color, true, profile); }
        private void Band(int color, params float[] profile)
        { LatheSurface(color, false, profile); }
        private static Point ProfileNormal(float[] profile, int segment, double angle)
        {
            float dy = profile[(segment + 1) * 2] - profile[segment * 2];
            float dr = profile[(segment + 1) * 2 + 1] - profile[segment * 2 + 1];
            return Unit(new Point((float)Math.Sin(angle) * dy, -dr, -(float)Math.Cos(angle) * dy));
        }
        private static Point SmoothProfileNormal(float[] profile, int ring, int segment, double angle)
        {
            var current = ProfileNormal(profile, segment, angle);
            int neighbour = ring == segment ? segment - 1 : segment + 1;
            if (neighbour < 0 || neighbour >= profile.Length / 2 - 1) return current;
            var other = ProfileNormal(profile, neighbour, angle);
            // Smooth round shoulders, but retain manufactured seams and right-angle lid edges.
            if (current.X * other.X + current.Y * other.Y + current.Z * other.Z < .65f) return current;
            return Unit(new Point(current.X + other.X, current.Y + other.Y, current.Z + other.Z));
        }
        private void LatheSurface(int color, bool caps, float[] profile)
        {
            for (int ring=0;ring<profile.Length/2-1;ring++)
                for (int j=0;j<Segments;j++)
                {
                    double a=j*Math.PI*2/Segments, b=(j+1)*Math.PI*2/Segments;
                    int start = Vertices.Count;
                    Quad(Ring(profile[ring*2],profile[ring*2+1],a),
                        Ring(profile[ring*2+2],profile[ring*2+3],a),
                        Ring(profile[ring*2+2],profile[ring*2+3],b),
                        Ring(profile[ring*2],profile[ring*2+1],b),color);
                    Normals[start] = SmoothProfileNormal(profile, ring, ring, a);
                    Normals[start+1] = SmoothProfileNormal(profile, ring+1, ring, a);
                    Normals[start+2] = SmoothProfileNormal(profile, ring+1, ring, b);
                    Normals[start+3] = SmoothProfileNormal(profile, ring, ring, b);
                }
            if (!caps) return;
            for (int j=0;j<Segments;j++)
            {
                double a=j*Math.PI*2/Segments,b=(j+1)*Math.PI*2/Segments;
                var bottom=new Point(0,profile[0],0);
                Quad(bottom,Ring(profile[0],profile[1],a),Ring(profile[0],profile[1],b),bottom,color);
                int last=profile.Length-2; var top=new Point(0,profile[last],0);
                Quad(top,Ring(profile[last],profile[last+1],b),Ring(profile[last],profile[last+1],a),top,color);
            }
        }
        private void Label(int label,float w,float h,float y,float z)
        {
            Quad(new Point(-w/2,y-h/2,z),new Point(-w/2,y+h/2,z),
                new Point(w/2,y+h/2,z),new Point(w/2,y-h/2,z),
                LabelUV(label,0,0),LabelUV(label,0,1),LabelUV(label,1,1),LabelUV(label,1,0));
        }
        private void CurvedLabel(int label,float radius,float bottom,float top)
        {
            int panels = Segments / 3;
            for (int i=0;i<panels;i++)
            {
                double a=(i-panels/2)*Math.PI*2/Segments, b=(i+1-panels/2)*Math.PI*2/Segments;
                float u=i/(float)panels,v=(i+1)/(float)panels;
                int start = Vertices.Count;
                Quad(Ring(bottom,radius,a),Ring(top,radius,a),Ring(top,radius,b),Ring(bottom,radius,b),
                    LabelUV(label,u,0),LabelUV(label,u,1),LabelUV(label,v,1),LabelUV(label,v,0));
                Normals[start] = Normals[start+1] = new Point((float)Math.Sin(a),0,-(float)Math.Cos(a));
                Normals[start+2] = Normals[start+3] = new Point((float)Math.Sin(b),0,-(float)Math.Cos(b));
            }
        }

        private static float PouchZ(float x, float y, float center, float width, float height, float depth)
        {
            float nx = Math.Max(-1, Math.Min(1, x * 2 / width)), ny = Math.Max(-1, Math.Min(1, (y - center) * 2 / height));
            float fullness = .35f + .65f * (float)(Math.Cos(nx * Math.PI / 2) * Math.Cos(ny * Math.PI / 2));
            float folds = (float)(Math.Sin(ny * 9 + nx * 5) * Math.Pow(Math.Abs(nx), 4) * Math.Max(0, 1 - ny * ny));
            return -depth * .5f * fullness + depth * .025f * folds;
        }
        private static Point PouchNormal(Point p, float center, float width, float height, float depth, bool back = false)
        {
            const float step = .0001f;
            float dx = (PouchZ(p.X + step,p.Y,center,width,height,depth) - PouchZ(p.X - step,p.Y,center,width,height,depth)) / (2 * step);
            float dy = (PouchZ(p.X,p.Y + step,center,width,height,depth) - PouchZ(p.X,p.Y - step,center,width,height,depth)) / (2 * step);
            return Unit(new Point(dx,dy,back ? 1 : -1));
        }
        private void Pouch(float x, float center, float width, float height, float depth, int color)
        {
            const int rows = 8, columns = 8;
            var front = new Point[columns+1,rows+1];
            for (int row=0;row<=rows;row++) for (int column=0;column<=columns;column++)
            {
                float ny=row*2f/rows-1, nx=column*2f/columns-1;
                float px=x+nx*width*.5f*(1-.045f*(float)Math.Pow(Math.Abs(ny),4));
                float py=center+ny*height*.5f;
                front[column,row]=new Point(px,py,PouchZ(px-x,py,center,width,height,depth));
            }
            for (int row=0;row<rows;row++) for (int column=0;column<columns;column++)
            {
                var a=front[column,row]; var b=front[column+1,row];
                var c=front[column+1,row+1]; var d=front[column,row+1];
                int start=Vertices.Count; Quad(a,d,c,b,color);
                for(int i=0;i<4;i++) Normals[start+i]=PouchNormal(Vertices[start+i],center,width,height,depth);
                var ab=new Point(a.X,a.Y,-a.Z); var bb=new Point(b.X,b.Y,-b.Z);
                var cb=new Point(c.X,c.Y,-c.Z); var db=new Point(d.X,d.Y,-d.Z);
                start=Vertices.Count; Quad(ab,bb,cb,db,color);
                for(int i=0;i<4;i++) Normals[start+i]=PouchNormal(Vertices[start+i],center,width,height,depth,true);
                if(row==0) Quad(a,b,bb,ab,color);
                if(row==rows-1) Quad(d,db,cb,c,color);
                if(column==0) Quad(a,ab,db,d,color);
                if(column==columns-1) Quad(b,c,cb,bb,color);
            }
        }
        private void PouchLabel(int label,float width,float height,float center,float pouchWidth,float pouchHeight,float depth)
        {
            const int columns=6,rows=6;
            for(int row=0;row<rows;row++) for(int column=0;column<columns;column++)
            {
                float u=column/(float)columns,v=row/(float)rows,un=(column+1)/(float)columns,vn=(row+1)/(float)rows;
                float left=(u-.5f)*width,right=(un-.5f)*width,bottom=center+(v-.5f)*height,top=center+(vn-.5f)*height;
                var a=new Point(left,bottom,PouchZ(left,bottom,center,pouchWidth,pouchHeight,depth)-.00025f);
                var b=new Point(left,top,PouchZ(left,top,center,pouchWidth,pouchHeight,depth)-.00025f);
                var c=new Point(right,top,PouchZ(right,top,center,pouchWidth,pouchHeight,depth)-.00025f);
                var d=new Point(right,bottom,PouchZ(right,bottom,center,pouchWidth,pouchHeight,depth)-.00025f);
                int start=Vertices.Count;
                Quad(a,b,c,d,LabelUV(label,u,v),LabelUV(label,u,vn),LabelUV(label,un,vn),LabelUV(label,un,v));
                for(int i=0;i<4;i++) Normals[start+i]=PouchNormal(Vertices[start+i],center,pouchWidth,pouchHeight,depth);
            }
        }
        private void CarryStrap(float center,float radiusX,float radiusY,float width,float depth,float z)
        {
            for(int i=0;i<12;i++)
            {
                double a=i*Math.PI/12,b=(i+1)*Math.PI/12;
                var ao=new Point((float)Math.Cos(a)*(radiusX+width*.5f),center+(float)Math.Sin(a)*(radiusY+width*.5f),z-depth*.5f);
                var bo=new Point((float)Math.Cos(b)*(radiusX+width*.5f),center+(float)Math.Sin(b)*(radiusY+width*.5f),z-depth*.5f);
                var ai=new Point((float)Math.Cos(a)*(radiusX-width*.5f),center+(float)Math.Sin(a)*(radiusY-width*.5f),z-depth*.5f);
                var bi=new Point((float)Math.Cos(b)*(radiusX-width*.5f),center+(float)Math.Sin(b)*(radiusY-width*.5f),z-depth*.5f);
                Quad(ai,bi,bo,ao,9);
                Quad(new Point(ai.X,ai.Y,z+depth*.5f),new Point(ao.X,ao.Y,z+depth*.5f),new Point(bo.X,bo.Y,z+depth*.5f),new Point(bi.X,bi.Y,z+depth*.5f),9);
                Quad(ao,bo,new Point(bo.X,bo.Y,z+depth*.5f),new Point(ao.X,ao.Y,z+depth*.5f),9);
                Quad(ai,new Point(ai.X,ai.Y,z+depth*.5f),new Point(bi.X,bi.Y,z+depth*.5f),bi,9);
            }
        }
        private void SideHandle(float center,float x,float radiusX,float radiusY,float radius)
        {
            const int along=12,around=8;
            for(int i=0;i<along;i++) for(int j=0;j<around;j++)
            {
                double a=-Math.PI/2+i*Math.PI/along,b=-Math.PI/2+(i+1)*Math.PI/along;
                double pa=j*Math.PI*2/around,pb=(j+1)*Math.PI*2/around;
                int start=Vertices.Count;
                Quad(HandlePoint(a,pa,center,x,radiusX,radiusY,radius),HandlePoint(b,pa,center,x,radiusX,radiusY,radius),
                    HandlePoint(b,pb,center,x,radiusX,radiusY,radius),HandlePoint(a,pb,center,x,radiusX,radiusY,radius),9);
                Normals[start]=HandleNormal(a,pa,radiusX,radiusY); Normals[start+1]=HandleNormal(b,pa,radiusX,radiusY);
                Normals[start+2]=HandleNormal(b,pb,radiusX,radiusY); Normals[start+3]=HandleNormal(a,pb,radiusX,radiusY);
            }
            Box(x-.001f,center-radiusY,0,.008f,.008f,.012f,9);
            Box(x-.001f,center+radiusY,0,.008f,.008f,.012f,9);
        }
        private static Point HandleNormal(double angle,double around,float radiusX,float radiusY)
        {
            var normal=Unit(new Point((float)Math.Cos(angle)/radiusX,(float)Math.Sin(angle)/radiusY,0));
            return new Point(normal.X*(float)Math.Cos(around),normal.Y*(float)Math.Cos(around),(float)Math.Sin(around));
        }
        private static Point HandlePoint(double angle,double around,float center,float x,float radiusX,float radiusY,float radius)
        {
            var normal=HandleNormal(angle,around,radiusX,radiusY);
            return new Point(x+(float)Math.Cos(angle)*radiusX+normal.X*radius,
                center+(float)Math.Sin(angle)*radiusY+normal.Y*radius,normal.Z*radius);
        }
    }
}
