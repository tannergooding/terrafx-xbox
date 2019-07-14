using System.Numerics;

namespace TestApp
{
    // Constant buffer used to send MVP matrices to the vertex shader.
    public struct ModelViewProjectionConstantBuffer
    {
        public Matrix4x4 model;
        public Matrix4x4 view;
        public Matrix4x4 projection;
    }
}
