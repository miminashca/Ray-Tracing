using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using static UnityEngine.Mathf;

public static class ShaderHelper
{
	public static readonly GraphicsFormat RGBA_SFloat = GraphicsFormat.R32G32B32A32_SFloat;
	public enum DepthMode { None = 0, Depth16 = 16, Depth24 = 24 }

	// ComputeShaders
	/// Convenience method for dispatching a compute shader.
	/// It calculates the number of thread groups based on the number of iterations needed.
	public static void Dispatch(ComputeShader cs, int numIterationsX, int numIterationsY = 1, int numIterationsZ = 1, int kernelIndex = 0)
	{
		Vector3Int threadGroupSizes = GetThreadGroupSizes(cs, kernelIndex);
		int numGroupsX = Mathf.CeilToInt(numIterationsX / (float)threadGroupSizes.x);
		int numGroupsY = Mathf.CeilToInt(numIterationsY / (float)threadGroupSizes.y);
		int numGroupsZ = Mathf.CeilToInt(numIterationsZ / (float)threadGroupSizes.y);
		cs.Dispatch(kernelIndex, numGroupsX, numGroupsY, numGroupsZ);
	}
	public static Vector3Int GetThreadGroupSizes(ComputeShader compute, int kernelIndex = 0)
	{
		uint x, y, z;
		compute.GetKernelThreadGroupSizes(kernelIndex, out x, out y, out z);
		return new Vector3Int((int)x, (int)y, (int)z);
	}
	
	
	
	// Init
	public static void InitMaterial(Shader shader, ref Material mat)
	{
		if (mat == null || (mat.shader != shader && shader != null))
		{
			if (shader == null)
			{
				shader = Shader.Find("Unlit/Texture");
			}

			mat = new Material(shader);
		}
	}
	
	
	
	// Create Buffers
	// Create a compute buffer containing the given data (Note: data must be blittable)
	public static void CreateStructuredBuffer<T>(ref ComputeBuffer buffer, T[] data) where T : struct
	{
		// Cannot create 0 length buffer (not sure why?)
		int length = Max(1, data.Length);
		// The size (in bytes) of the given data type
		int stride = System.Runtime.InteropServices.Marshal.SizeOf(typeof(T));

		// If buffer is null, wrong size, etc., then we'll need to create a new one
		if (buffer == null || !buffer.IsValid() || buffer.count != length || buffer.stride != stride)
		{
			if (buffer != null) { buffer.Release(); }
			buffer = new ComputeBuffer(length, stride, ComputeBufferType.Structured);
		}

		buffer.SetData(data);
	}
	// Create a compute buffer containing the given data (Note: data must be blittable)
	public static void CreateStructuredBuffer<T>(ref ComputeBuffer buffer, List<T> data) where T : struct
	{
		// Cannot create 0 length buffer (not sure why?)
		int length = Max(1, data.Count);
		// The size (in bytes) of the given data type
		int stride = System.Runtime.InteropServices.Marshal.SizeOf(typeof(T));

		// If buffer is null, wrong size, etc., then we'll need to create a new one
		if (buffer == null || !buffer.IsValid() || buffer.count != length || buffer.stride != stride)
		{
			if (buffer != null) { buffer.Release(); }
			buffer = new ComputeBuffer(length, stride, ComputeBufferType.Structured);
		}

		buffer.SetData(data);
	}
	
	
	// Create Render Texture
	public static RenderTexture CreateRenderTexture(int width, int height, FilterMode filterMode, GraphicsFormat format, string name = "Unnamed", DepthMode depthMode = DepthMode.None, bool useMipMaps = false)
	{
		RenderTexture texture = new RenderTexture(width, height, (int)depthMode);
		texture.graphicsFormat = format;
		texture.enableRandomWrite = true;
		texture.autoGenerateMips = false;
		texture.useMipMap = useMipMaps;
		texture.Create();

		texture.name = name;
		texture.wrapMode = TextureWrapMode.Clamp;
		texture.filterMode = filterMode;
		return texture;
	}
	public static bool CreateRenderTexture(ref RenderTexture texture, int width, int height, FilterMode filterMode, GraphicsFormat format, string name = "Unnamed", DepthMode depthMode = DepthMode.None, bool useMipMaps = false)
	{
		if (texture == null || !texture.IsCreated() || texture.width != width || texture.height != height || texture.graphicsFormat != format || texture.depth != (int)depthMode || texture.useMipMap != useMipMaps)
		{
			if (texture != null)
			{
				texture.Release();
			}
			texture = CreateRenderTexture(width, height, filterMode, format, name, depthMode, useMipMaps);
			return true;
		}
		else
		{
			texture.name = name;
			texture.wrapMode = TextureWrapMode.Clamp;
			texture.filterMode = filterMode;
		}

		return false;
	}
	
	
	// Release
	public static void Release(ComputeBuffer buffer)
	{
		if (buffer != null)
		{
			buffer.Release();	
		}
	}
	public static void Release(params ComputeBuffer[] buffers)
	{
		for (int i = 0; i < buffers.Length; i++)
		{
			Release(buffers[i]);
		}
	}
	public static void Release(RenderTexture tex)
	{
		if (tex != null)
		{
			tex.Release();
		}
	}

}
