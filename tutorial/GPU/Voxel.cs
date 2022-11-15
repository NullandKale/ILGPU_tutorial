namespace tutorial.GPU
{
    public struct Voxel
    {
        public float r;
        public float g;
        public float b;
        public byte hasLeftData;
        public byte hasRightData;

        public Voxel()
        {
            r = 0;
            g = 0;
            b = 0;
            hasLeftData = 0;
            hasRightData = 0;
        }

        public Voxel(byte r, byte g, byte b)
        {
            this.r = r / 255.0f;
            this.g = g / 255.0f;
            this.b = b / 255.0f;
            hasLeftData = 0;
            hasRightData = 0;
        }

        public Voxel(float r, float g, float b)
        {
            this.r = r;
            this.g = g;
            this.b = b;
            hasLeftData = 0;
            hasRightData = 0;
        }

        public Voxel(float r, float g, float b, bool isLeft)
        {
            this.r = r;
            this.g = g;
            this.b = b;

            if(isLeft)
            {
                hasLeftData = 1;
                hasRightData = 0;
            }
            else
            {
                hasLeftData = 0;
                hasRightData = 1;
            }
        }

        public bool isHit()
        {
            //return r > 0 || g > 0 || b > 0;
            return isAfterLeftCameraData() && isAfterRightCameraData();
        }

        public bool isAfterLeftCameraData()
        {
            return hasLeftData == 1;
        }

        public void SetAfterLeftCameraData(bool val)
        {
            if (val)
            {
                hasLeftData = 1;
            }
            else
            {
                hasLeftData = 0;
            }
        }

        public bool isAfterRightCameraData()
        {
            return hasRightData == 1;
        }

        public void SetAfterRightCameraData(bool val)
        {
            if (val)
            {
                hasRightData = 1;
            }
            else
            {
                hasRightData = 0;
            }
        }
    }
}
