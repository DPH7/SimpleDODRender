using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class WeaponManager : MonoBehaviour
{
    [Header("武器预制体")]
    public ReflectBall reflectBallPrefab;
    public BezierLaser bezierLaserPrefab; // 拖入挂载了 BezierLaser 的激光预制体

    [Header("生成范围 (与地图边界对应)")]
    public Vector2 spawnMinBounds = new Vector2(-15f, -15f);
    public Vector2 spawnMaxBounds = new Vector2(15f, 15f);

    void Start()
    {
        // 示例：启动时生成反弹球和一条激光
    }

    void Update()
    {
        // 按 Space 生成反弹球
        if (Input.GetKeyDown(KeyCode.Space))
        {
            SpawnReflectBallAtRandomPosition();
        }

        // 按 J 键测试发射一条随机路径的贝塞尔激光
        if (Input.GetKeyDown(KeyCode.J))
        {
            SpawnBezierLaserAtRandomPosition();
        }
    }

    /// <summary>
    /// 在指定范围内随机生成一个发射 ReflectBall
    /// </summary>
    public void SpawnReflectBallAtRandomPosition()
    {
        if (reflectBallPrefab == null)
        {
            Debug.LogError("ReflectBallPrefab 未赋值，请在 Inspector 中拖入预制体！");
            return;
        }

        float randomX = Random.Range(spawnMinBounds.x, spawnMaxBounds.x);
        float randomY = Random.Range(spawnMinBounds.y, spawnMaxBounds.y);
        Vector2 randomPos = new Vector2(randomX, randomY);

        ReflectBall ball = Instantiate(reflectBallPrefab);
        ball.InitAndLaunch(randomPos);
    }

    /// <summary>
    /// 生成并发射一条贝塞尔激光束
    /// </summary>
    public void SpawnBezierLaserAtRandomPosition()
    {
        if (bezierLaserPrefab == null)
        {
            Debug.LogError("BezierLaserPrefab 未赋值，请在 Inspector 中拖入预制体！");
            return;
        }

        // 1. 随机生成激光的起点与目标点
        Vector2 startPos = GetRandomPositionInBounds();
        Vector2 targetPos = GetRandomPositionInBounds();

        // 确保目标点与起点保持一定距离，避免太短
        while (Vector2.Distance(startPos, targetPos) < 5f)
        {
            targetPos = GetRandomPositionInBounds();
        }

        // 2. 实例化激光预制体
        BezierLaser laser = Instantiate(bezierLaserPrefab);

        // 3. 初始化并触发激光（传入起点、终点和持续时间 2 秒）
        laser.InitAndFire(startPos, targetPos, 2.0f);
    }

    /// <summary>
    /// 辅助方法：获取边界内的随机坐标
    /// </summary>
    private Vector2 GetRandomPositionInBounds()
    {
        float x = Random.Range(spawnMinBounds.x, spawnMaxBounds.x);
        float y = Random.Range(spawnMinBounds.y, spawnMaxBounds.y);
        return new Vector2(x, y);
    }
}