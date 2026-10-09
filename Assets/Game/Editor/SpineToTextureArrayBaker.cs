using System.IO;
using UnityEditor;
using UnityEngine;
using Spine.Unity;

public class SpineToTextureArrayBaker : EditorWindow
{
    private SkeletonDataAsset skeletonDataAsset;
    private string animationName = "walk";
    private int bakedFPS = 12;
    private int bakedWidth = 256;
    private int bakedHeight = 256;

    [MenuItem("Tools/DOD 2D/Bake Spine to Texture2DArray")]
    public static void ShowWindow()
    {
        GetWindow<SpineToTextureArrayBaker>("Spine 烘焙器");
    }

    private void OnGUI()
    {
        GUILayout.Label("Spine 离线烘焙配置", EditorStyles.boldLabel);

        skeletonDataAsset = (SkeletonDataAsset)EditorGUILayout.ObjectField("Spine 骨骼数据", skeletonDataAsset, typeof(SkeletonDataAsset), false);
        animationName = EditorGUILayout.TextField("动画名称", animationName);
        bakedFPS = EditorGUILayout.IntField("采样帧率 (FPS)", bakedFPS);
        bakedWidth = EditorGUILayout.IntField("烘焙宽度", bakedWidth);
        bakedHeight = EditorGUILayout.IntField("烘焙高度", bakedHeight);

        if (GUILayout.Button("开始离线烘焙 Texture2DArray"))
        {
            if (skeletonDataAsset == null)
            {
                Debug.LogError("请先指定 Spine 的 SkeletonDataAsset！");
                return;
            }
            BakeSpineAnimation();
        }
    }

    private void BakeSpineAnimation()
    {
        // 1. 实例化 Spine GameObject
        GameObject bakerGO = new GameObject("Temp_Spine_Baker");
        SkeletonAnimation skeletonAnim = bakerGO.AddComponent<SkeletonAnimation>();
        skeletonAnim.skeletonDataAsset = skeletonDataAsset;
        skeletonAnim.Initialize(true);

        // 验证动画是否存在
        var trackEntry = skeletonAnim.AnimationState.SetAnimation(0, animationName, false);
        if (trackEntry == null)
        {
            Debug.LogError($"未找到动画: {animationName}");
            DestroyImmediate(bakerGO);
            return;
        }

        float duration = trackEntry.Animation.Duration;
        int totalFrames = Mathf.CeilToInt(duration * bakedFPS);
        float timePerFrame = 1.0f / bakedFPS;

        // 2. 创建临时摄像机
        GameObject camGO = new GameObject("Temp_Baker_Camera");
        Camera cam = camGO.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0, 0, 0, 0); // 全透明背景
        cam.orthographic = true;
        cam.orthographicSize = 12f; // 缩放调节（确保怪物居中显示完整）
        camGO.transform.position = new Vector3(0.7f, 1.5f, -10); // 上移相机居中敌人

        RenderTexture rt = RenderTexture.GetTemporary(bakedWidth, bakedHeight, 24, RenderTextureFormat.ARGB32);
        cam.targetTexture = rt;

        // 3. 准备 Texture2DArray
        Texture2DArray texArray = new Texture2DArray(bakedWidth, bakedHeight, totalFrames, TextureFormat.RGBA32, false);
        texArray.filterMode = FilterMode.Bilinear;
        texArray.wrapMode = TextureWrapMode.Clamp;

        Texture2D tempReadTex = new Texture2D(bakedWidth, bakedHeight, TextureFormat.RGBA32, false);

        // 4. 离线驱动动画步进并采样
        for (int i = 0; i < totalFrames; i++)
        {
            float deltaTime = (i == 0) ? 0f : timePerFrame;

            // 核心修复点：强制更新 Spine 的 AnimationState 与 WorldTransform
            skeletonAnim.Update(deltaTime);
            skeletonAnim.LateUpdate();

            // 相机渲染
            cam.Render();

            // 读取 RT 像素
            RenderTexture.active = rt;
            tempReadTex.ReadPixels(new Rect(0, 0, bakedWidth, bakedHeight), 0, 0);
            tempReadTex.Apply();

            // 复制进 Texture2DArray
            Graphics.CopyTexture(tempReadTex, 0, 0, texArray, i, 0);
        }

        // 5. 保存资产与清理
        string outputPath = $"Assets/Baked_Spine_{animationName}_Array.asset";
        AssetDatabase.CreateAsset(texArray, outputPath);
        AssetDatabase.SaveAssets();

        RenderTexture.active = null;
        RenderTexture.ReleaseTemporary(rt);
        DestroyImmediate(tempReadTex);
        DestroyImmediate(camGO);
        DestroyImmediate(bakerGO);

        Debug.Log($"重新烘焙成功！共 {totalFrames} 帧已保存至 {outputPath}");
    }
}