using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class BezierLaser : MonoBehaviour
{
    [Header("激光属性")]
    public float damage = 20f;               // 激光伤害
    public float laserWidth = 0.3f;          // 激光判定宽度/半径
    public float damageInterval = 0.2f;      // 伤害判定间隔 (秒)
    public float laserDuration = 2f;         // 激光持续时间 (秒)

    [Header("贝塞尔采样配置")]
    [Range(5, 50)]
    public int segmentCount = 15;            // 曲线采样段数 (段数越多越平滑)
    public float controlPointOffset = 3f;    // 中间控制点弯曲偏移量

    private LineRenderer lineRenderer;
    private float damageTimer;
    private float durationTimer;

    // 缓存采样出的点，避免重复 new 数组
    private Vector2[] sampledPoints;
    // 缓存穿过的网格 Cell 索引
    private HashSet<int> tempCellIndices = new HashSet<int>();
    // 缓存当前帧已伤害过的敌人索引，防止重复扣血
    private HashSet<int> hitEnemyIndices = new HashSet<int>();

    private void Awake()
    {
        lineRenderer = GetComponent<LineRenderer>();
        sampledPoints = new Vector2[segmentCount + 1];
    }

    /// <summary>
    /// 初始化激光
    /// </summary>
    public void InitAndFire(Vector2 startPos, Vector2 targetPos, float duration)
    {
        this.laserDuration = duration;
        this.durationTimer = duration;
        this.damageTimer = 0f;

        lineRenderer.startWidth = laserWidth;
        lineRenderer.endWidth = laserWidth;
        gameObject.SetActive(true);

        // 更新并渲染激光
        UpdateLaserShape(startPos, targetPos);
    }

    private void Update()
    {
        durationTimer -= Time.deltaTime;
        if (durationTimer <= 0f)
        {
            gameObject.SetActive(false);
            return;
        }

        // 假定起点跟随玩家，目标点跟随鼠标/敌人，这里可在 Update 中持续更新
        // UpdateLaserShape(startPos, targetPos);

        // 伤害定时器控制 (按照帧间隔/Tick进行伤害刷新)
        damageTimer -= Time.deltaTime;
        if (damageTimer <= 0f)
        {
            CheckLaserCollision();
            damageTimer = damageInterval; // 重置伤害触发间隔
        }
    }

    /// <summary>
    /// 1. 采样二次贝塞尔曲线并更新 LineRenderer
    /// </summary>
    public void UpdateLaserShape(Vector2 startPos, Vector2 targetPos)
    {
        // 自动计算一个垂直于连线的中间控制点 (产生弧度弯曲)
        Vector2 dir = targetPos - startPos;
        Vector2 normal = new Vector2(-dir.y, dir.x).normalized;
        Vector2 controlPos = (startPos + targetPos) * 0.5f + normal * controlPointOffset;

        lineRenderer.positionCount = segmentCount + 1;

        for (int i = 0; i <= segmentCount; i++)
        {
            float t = (float)i / segmentCount;
            // 二次贝塞尔曲线公式: B(t) = (1-t)^2 * P0 + 2(1-t)t * P1 + t^2 * P2
            Vector2 point = Mathf.Pow(1 - t, 2) * startPos + 
                            2 * (1 - t) * t * controlPos + 
                            Mathf.Pow(t, 2) * targetPos;

            sampledPoints[i] = point;
            lineRenderer.SetPosition(i, new Vector3(point.x, point.y, 0f));
        }
    }

    /// <summary>
    /// 2. 结合 Spatial Hash 进行多段线碰撞检测
    /// </summary>
    private void CheckLaserCollision()
    {
        if (SpineDODRenderManager.Instance == null) return;

        hitEnemyIndices.Clear(); // 清空本轮已受击的敌人集合

        // 遍历贝塞尔曲线折线段
        for (int i = 0; i < segmentCount; i++)
        {
            Vector2 p1 = sampledPoints[i];
            Vector2 p2 = sampledPoints[i + 1];

            // A. 粗筛：获取线段 p1 -> p2 经过的所有网格 Cell
            tempCellIndices.Clear();
            SpineDODRenderManager.Instance.GetGridCellsOnLine(p1, p2, tempCellIndices);

            // B. 遍历这些 Cell 里的敌人
            foreach (int cellIndex in tempCellIndices)
            {
                SpineDODRenderManager.Instance.GetGridEnemyOnLine(p1, p2, laserWidth * 0.5f, cellIndex, hitEnemyIndices);
            }
            foreach (var hitEnemyIndex in hitEnemyIndices)
            {
                SpineDODRenderManager.Instance.OnHitEnemy(hitEnemyIndex, damage);   
            }
        }
    }
}