using System;

namespace com.zibra.liquid.DataStructures
{
    [Serializable]
    public class MeshRepresentation
    {
        public string vertices;
        public string faces;
        public int vox_dim;
        public int sdf_dim;
    }
}