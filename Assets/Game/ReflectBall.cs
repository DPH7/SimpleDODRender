using UnityEngine;
using System.Collections.Generic;
public class ReflectBall : MonoBehaviour
{
    [Header("基础属性")]
    public float moveSpeed = 8f;        // 恒定移动速度
    public float damage = 10f;          // 伤害值
    public float radius = 0.3f;         // 碰撞判定半径

    [Header("屏幕边缘反弹")]
    public Camera mainCamera;           // 主相机引用
    public float screenMargin = 0.2f;   // 边缘留白/半径修正，防止球体一半穿出屏幕外

    private Vector2 minBounds;
    private Vector2 maxBounds;
    private Vector2 currentVelocity;    // 当前速度向量

    private readonly List<int> _hitResults = new List<int>(32); // 预分配容量
    
    private void Start()
    {
        if (mainCamera == null)
        {
            mainCamera = Camera.main;
        }

        // 更新屏幕四角对应的世界坐标边界
        UpdateScreenBounds();
    }

    /// <summary>
    /// 根据当前相机视角计算屏幕视口的世界坐标边界
    /// </summary>
    public void UpdateScreenBounds()
    {
        if (mainCamera == null) return;

        // 获取屏幕左下角和右上角对应的世界坐标 (Z轴取 0)
        Vector3 bottomLeft = mainCamera.ViewportToWorldPoint(new Vector3(0, 0, Mathf.Abs(mainCamera.transform.position.z)));
        Vector3 topRight = mainCamera.ViewportToWorldPoint(new Vector3(1, 1, Mathf.Abs(mainCamera.transform.position.z)));

        // 减去一定 margin (例如球体自身半径)，确保完全在屏幕内反弹
        minBounds = new Vector2(bottomLeft.x + screenMargin, bottomLeft.y + screenMargin);
        maxBounds = new Vector2(topRight.x - screenMargin, topRight.y - screenMargin);
    }

    /// <summary>
    /// 初始化并发射反弹球
    /// </summary>
    public void InitAndLaunch(Vector2 spawnPos)
    {
        if (mainCamera == null) mainCamera = Camera.main;
        UpdateScreenBounds();

        transform.position = spawnPos;

        // 随机生成 2D 运动方向
        float randomAngle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
        Vector2 randomDir = new Vector2(Mathf.Cos(randomAngle), Mathf.Sin(randomAngle)).normalized;

        currentVelocity = randomDir * moveSpeed;
        gameObject.SetActive(true);
    }

    private void Update()
    {
        // 1. 平移
        transform.Translate(currentVelocity * Time.deltaTime, Space.World);

        // 2. 检测并处理屏幕边缘反弹
        HandleBoundaryReflection();

        // 3. 碰撞敌人检测
        CheckEnemyCollision();
    }

    private void HandleBoundaryReflection()
    {
        Vector3 pos = transform.position;
        bool bounced = false;

        // 左右屏幕边缘反弹
        if (pos.x <= minBounds.x && currentVelocity.x < 0)
        {
            currentVelocity.x = -currentVelocity.x;
            pos.x = minBounds.x;
            bounced = true;
        }
        else if (pos.x >= maxBounds.x && currentVelocity.x > 0)
        {
            currentVelocity.x = -currentVelocity.x;
            pos.x = maxBounds.x;
            bounced = true;
        }

        // 上下屏幕边缘反弹
        if (pos.y <= minBounds.y && currentVelocity.y < 0)
        {
            currentVelocity.y = -currentVelocity.y;
            pos.y = minBounds.y;
            bounced = true;
        }
        else if (pos.y >= maxBounds.y && currentVelocity.y > 0)
        {
            currentVelocity.y = -currentVelocity.y;
            pos.y = maxBounds.y;
            bounced = true;
        }

        if (bounced)
        {
            transform.position = pos;
        }
    }

    private void CheckEnemyCollision()
    {
        // 复用单例的碰撞检测逻辑
        if (SpineDODRenderManager.Instance == null) return;
        
        // 此处可调用前面改好的 CheckCollision，传入成员 List 避免 GC
        SpineDODRenderManager.Instance.CheckCollision(transform.position, radius, _hitResults);
        
        for (int i = 0; i < _hitResults.Count; i++)
        {
            int enemyIndex = _hitResults[i];
            // 扣血逻辑
            SpineDODRenderManager.Instance.OnHitEnemy(enemyIndex, damage);
        }
    }

    private void OnDrawGizmosSelected()
    {
        // 绘制实际检测到的屏幕反弹框
        Gizmos.color = Color.green;
        Vector3 size = new Vector3(maxBounds.x - minBounds.x, maxBounds.y - minBounds.y, 0);
        Vector3 center = new Vector3((minBounds.x + maxBounds.x) * 0.5f, (minBounds.y + maxBounds.y) * 0.5f, 0);
        Gizmos.DrawWireCube(center, size);
    }
}