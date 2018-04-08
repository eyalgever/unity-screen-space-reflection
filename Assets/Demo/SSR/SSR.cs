﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;

//[ExecuteInEditMode]
public class SSR : MonoBehaviour
{
    enum Pass { depth, reflection, xblur, yblur, accumulation, composition }
    enum ViewMode { Original, Normal, Reflection, CalcCount, MipMap, Diffuse, Speclar, Occlusion, Smmothness, Emission }

    [Header("Reflection")]
    [SerializeField]
    Shader shader;
    [SerializeField] ViewMode viewMode;
    [SerializeField] [Range(0, 5)] int maxLOD = 3;
    [SerializeField] [Range(0, 150)] int maxLoop = 150;
    [SerializeField] [Range(0.001f, 0.01f)] float baseRaise = 0.001f;
    [SerializeField] [Range(0.001f, 0.01f)] float thickness = 0.006f;
    [SerializeField] [Range(0.01f, 0.1f)] float rayLengthCoeff = 0.06f;

    [Header("Blur")]
    [SerializeField] int blurNum;
    [SerializeField] [Range(0.0f, 0.1f)] float blurThreshold;
    [SerializeField] [Range(0, 1)] float reflectionRate;

    Material mat;
    RenderTexture dpt;
    Camera cam;
    Mesh quad;
    RenderTexture[] rts = new RenderTexture[2];

    int Width { get { return GetComponent<Camera>().pixelWidth; } }
    int Height { get { return GetComponent<Camera>().pixelHeight; } }

    Mesh CreateQuad()
    {
        Mesh mesh = new Mesh();
        mesh.name = "Quad";
        mesh.vertices = new Vector3[]
        {
            new Vector3(1f, 1f, 0f),
            new Vector3(-1f, 1f, 0f),
            new Vector3(-1f,-1f, 0f),
            new Vector3(1f, -1f, 0f),
        };
        mesh.triangles = new int[]
        {
            0, 1, 2,
            2, 3, 0
        };
        return mesh;
    }

    void OnEnable()
    {
        mat = new Material(shader);
        dpt = new RenderTexture(Screen.width, Screen.height, 24);
        dpt.useMipMap = true;
        dpt.autoGenerateMips = true;
        dpt.enableRandomWrite = true;
        dpt.filterMode = FilterMode.Bilinear;
        dpt.Create();
        cam = GetComponent<Camera>();
        quad = CreateQuad();
    }

    void OnDisable()
    {
        Destroy(mat);
        dpt.Release();
    }

    void Update()
    {
        var resolution = new Vector2Int(cam.pixelWidth, cam.pixelHeight);

        if (dpt != null && (dpt.width != resolution.x || dpt.height != resolution.y))
        {
            dpt.Release();
        }

        if (dpt == null || !dpt.IsCreated())
        {
            dpt = new RenderTexture(Width, Height, 24);
            dpt.useMipMap = true;
            dpt.autoGenerateMips = true;
            dpt.enableRandomWrite = true;
            dpt.filterMode = FilterMode.Bilinear;
            dpt.Create();
        }

        for (int i = 0; i < 2; ++i)
        {
            if (rts[i] != null && (
                rts[i].width != (int)resolution.x ||
                rts[i].height != (int)resolution.y
            ))
            {
                if (rts[i] != null)
                {
                    rts[i].Release();
                    rts[i] = null;
                }
            }

            if (rts[i] == null || !rts[i].IsCreated())
            {
                rts[i] = new RenderTexture((int)resolution.x, (int)resolution.y, 0, RenderTextureFormat.ARGB32);
                rts[i].filterMode = FilterMode.Bilinear;
                rts[i].useMipMap = false;
                rts[i].autoGenerateMips = false;
                rts[i].enableRandomWrite = true;
                rts[i].Create();
                Graphics.SetRenderTarget(rts[i]);
                GL.Clear(false, true, new Color(0, 0, 0, 0));
            }
        }
    }

    void OnRenderImage(RenderTexture src, RenderTexture dst)
    {
        Graphics.Blit(src, dpt, mat, (int)Pass.depth);

        // world <-> screen matrix
        var view = cam.worldToCameraMatrix;
        var proj = GL.GetGPUProjectionMatrix(cam.projectionMatrix, false);
        var viewProj = proj * view;
        mat.SetMatrix("_ViewProj", viewProj);
        mat.SetMatrix("_InvViewProj", viewProj.inverse);

        mat.SetFloat("_BaseRaise", baseRaise);
        mat.SetFloat("_Thickness", thickness);
        mat.SetFloat("_RayLenCoeff", rayLengthCoeff);
        mat.SetInt("_ViewMode", (int)viewMode);
        mat.SetInt("_MaxLOD", maxLOD);
        mat.SetInt("_MaxLoop", maxLoop);
        mat.SetFloat("_TimeElapsed", Time.time);
        mat.SetTexture("_CameraDepthMipmap", dpt);


        RenderTexture reflectionTexture = RenderTexture.GetTemporary(Width, Height, 24, RenderTextureFormat.ARGB32);
        RenderTexture xBlurTexture = RenderTexture.GetTemporary(Width, Height, 24, RenderTextureFormat.ARGB32);
        RenderTexture yBlurTexture = RenderTexture.GetTemporary(Width, Height, 24, RenderTextureFormat.ARGB32);
        reflectionTexture.filterMode = FilterMode.Bilinear;
        xBlurTexture.filterMode = FilterMode.Bilinear;
        yBlurTexture.filterMode = FilterMode.Bilinear;

        Graphics.Blit(src, reflectionTexture, mat, (int)Pass.reflection);
        mat.SetTexture("_ReflectionTexture", reflectionTexture);

        mat.SetFloat("_BlurThreshold", blurThreshold);
        mat.SetFloat("_ReflectionRate", reflectionRate);

        for(var i =0; i < blurNum; i++)
        {
            Graphics.SetRenderTarget(xBlurTexture);
            mat.SetPass((int)Pass.xblur);
            Graphics.DrawMeshNow(quad, Matrix4x4.identity);
            mat.SetTexture("_ReflectionTexture", xBlurTexture);

            Graphics.SetRenderTarget(yBlurTexture);
            mat.SetPass((int)Pass.yblur);
            Graphics.DrawMeshNow(quad, Matrix4x4.identity);
            mat.SetTexture("_ReflectionTexture", yBlurTexture);
        }

        mat.SetTexture("_PreAccumulationTexture", rts[1]);
        Graphics.SetRenderTarget(rts[0]);
        mat.SetPass((int)Pass.accumulation);
        Graphics.DrawMeshNow(quad, Matrix4x4.identity);

        mat.SetTexture("_AccumulationTexture", rts[0]);
        Graphics.SetRenderTarget(dst);
        Graphics.Blit(src, dst, mat, (int)Pass.composition);

        RenderTexture.ReleaseTemporary(reflectionTexture);
        RenderTexture.ReleaseTemporary(xBlurTexture);
        RenderTexture.ReleaseTemporary(yBlurTexture);

        RenderTexture tmp = rts[1];
        rts[1] = rts[0];
        rts[0] = tmp;
    }
}
