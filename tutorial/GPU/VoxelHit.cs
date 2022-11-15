using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace tutorial.GPU
{
    public struct VoxelHit
    {
        public Voxel v;
        public int hitCount;

        public VoxelHit()
        {
            v = new Voxel();
            hitCount = 0;
        }

        public void Hit(Voxel v)
        {
            hitCount++;
            this.v.r += v.r;
            this.v.g += v.g;
            this.v.b += v.b;
        }

        public Vec3 GetColormappedColor(bool useAces)
        {
            Vec3 color;

            if (useAces)
            {
                color = Vec3.aces_approx(new Vec3(v.r, v.g, v.b) / hitCount / 2f);
            }
            else
            {
                color = Vec3.reinhard(new Vec3(v.r, v.g, v.b) / hitCount / 2f);
            }

            color *= 255f;

            return color;
        }

        public Vec3 GetColor()
        {
            Vec3 color = new Vec3(v.r, v.g, v.b);
            
            color /= (float)hitCount;
            color /= 2f;
            color *= 255f;

            return color;
        }
    }
}
