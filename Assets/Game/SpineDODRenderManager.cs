using Unity.Collections;
using Unity.Jobs;
using Unity.Burst;
using Unity.Mathematics;
using UnityEngine;
using System.Collections.Generic;

// 1. 2D 敌人的动画与渲染数据
public struct EnemyRenderData2D
{
    public float animTimer;    // 当前累计时间
    public float animSpeed;    // 播放速度 (FPS)
    public int totalFrame;    // 总帧数
}

// 1. 【极热数据 - 移动与物理模块】(每帧必须访问，极度紧凑，仅 16 字节)
// 1 个 Cache Line (64B) 能一口气装下 4 个敌人的移动数据！
public struct EnemyTransformData {
    public float2 position; // 8 字节
    public float2 targetPos;  // 目标点（比如玩家当前位置）
    public float velocity; // 8 字节
}

// 2. 【温数据 - 战斗与生命值模块】(发生碰撞、受击、攻击触发时访问)
public struct EnemyCombatData {
    public float hp;
    public float maxHp;
}

// 3. 【温数据 - 复合属性面板】(计算伤害公式时才访问，50个字段打包在这里)
public struct EnemyStatsData {
    public float dodgeRate;      // 闪避
    public float critRate;       // 暴击
    public float critMultiplier; // 暴击伤害
    public float comboRate;      // 连击
    public float counterRate;    // 反击
    // ... 其他几十个属性字段统统打成一个大 Struct ...
}

// 4. 【冷数据 - 配置与标志位模块】(初始化、死亡、UI显示时访问)
public struct EnemyMetaInfo {
    public int configId;
    public bool isBoss;
    public int renderHandle;
}

public struct SpatialHashGrid
{
    public float cellSize; // 格子大小，建议设为敌人最大直径的 1.5~2 倍（如 2.0f）
    public int gridWidth; // 地图水平网格数量（如 100）
    public int gridHeight; // 地图垂直网格数量（如 100）

    // 【核心零 GC 结构】：使用一维数组存储链表头与节点指针
    // cellHeaders[cellIndex] 保存该格子内第一个敌人的 Index
    [ReadOnly]
    public NativeArray<int> cellHeaders;

    // nextEnemyIndices[enemyIndex] 保存同一个格子内下一个敌人的 Index
    [ReadOnly]
    public NativeArray<int> nextEnemyIndices;

    public void Initialize(int maxEnemies, float cellSize, int width, int height)
    {
        this.cellSize = cellSize;
        gridWidth = width;
        gridHeight = height;

        cellHeaders = new NativeArray<int>(maxEnemies, Allocator.Persistent);
        nextEnemyIndices = new NativeArray<int>(maxEnemies, Allocator.Persistent);
    }

    public void ClearAndBuildGrid(ref SpatialHashGrid grid, NativeArray<EnemyTransformData> transforms, int activeCount)
    {
        // 1. 清空所有格子的头指针 (-1 表示空)
        for (int i = 0; i < grid.cellHeaders.Length; i++)
        {
            grid.cellHeaders[i] = -1;
        }

        // 2. 将每个敌人插入对应的格子中 (单向链表平铺写入数组，0 GC Alloc!)
        for (int i = 0; i < activeCount; i++)
        {
            int cellIndex = PosToCellIndex(transforms[i].position);
            if (cellIndex >= 0 && cellIndex < grid.cellHeaders.Length)
            {
                // 头插法构建单向链表
                grid.nextEnemyIndices[i] = grid.cellHeaders[cellIndex];
                grid.cellHeaders[cellIndex] = i;
            }
        }
    }

    // 坐标映射到一维网格 Index
    private int PosToCellIndex(Vector2 pos)
    {
        // 转换为相对于网格左下角的局部坐标
        pos += new Vector2(gridWidth, gridHeight);
        
        int x = Mathf.FloorToInt(pos.x / cellSize);
        int y = Mathf.FloorToInt(pos.y / cellSize);

        // 防止超出地图边界
        if (x < 0 || x >= gridWidth || y < 0 || y >= gridHeight) return -1;
        return x + y * gridWidth;
    }

    public void ResolveEnemySeparationForSingle(
        int index,
        ref NativeArray<EnemyTransformData> transforms,
        in SpatialHashGrid grid,
        float enemyRadius)
    {
        float minDist = enemyRadius * 2f;
        float minDistSq = minDist * minDist;

        // 获取当前敌人的位置
        float2 posA = transforms[index].position;

        // 计算当前敌人所在的网格坐标
        int cellX = math.clamp((int)math.floor((posA.x + gridWidth) / grid.cellSize), 0, grid.gridWidth - 1);
        int cellY = math.clamp((int)math.floor((posA.y + gridHeight) / grid.cellSize), 0, grid.gridHeight - 1);

        float2 totalPushForce = float2.zero;

        // 遍历周围 3x3 范围内的 9 个格子
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                int targetCellX = cellX + dx;
                int targetCellY = cellY + dy;

                // 边界检查
                if (targetCellX < 0 || targetCellX >= grid.gridWidth ||
                    targetCellY < 0 || targetCellY >= grid.gridHeight) continue;

                int cellIndex = targetCellX + targetCellY * grid.gridWidth;
                int j = grid.cellHeaders[cellIndex]; // 只读访问

                // 沿链表遍历当前格子里的敌人
                while (j != -1)
                {
                    if (index != j) // 不与自己做检测
                    {
                        float2 posB = transforms[j].position;
                        float2 delta = posA - posB;
                        float distSq = math.lengthsq(delta); // 使用 Unity.Mathematics 的 lengthsq

                        if (distSq < minDistSq && distSq > 0.0001f)
                        {
                            float dist = math.sqrt(distSq);

                            // 双方重叠距离的一半（只推开自己这一半）
                            float overlap = (minDist - dist) * 0.5f;
                            float2 pushDir = delta / dist; // 归一化推力方向

                            // 累加所有邻居产生的排斥力
                            totalPushForce += pushDir * overlap;
                        }
                    }

                    j = grid.nextEnemyIndices[j]; // 移向格子内的下一个敌人
                }
            }
        }

        // 统一应用排斥偏移量并写回当前敌人
        var currentData = transforms[index];
        currentData.position += totalPushForce;
        transforms[index] = currentData;
    }
}

// 2. Burst 并行计算帧号与矩阵 Job
//[BurstCompile(CompileSynchronously = true)]
public struct Build2DInstanceJob : IJobParallelFor
{
    public float deltaTime;
    public NativeArray<EnemyRenderData2D> renderDatas;
    [WriteOnly] public NativeArray<float> frameIndices;

    public void Execute(int index)
    {
        var renderData = renderDatas[index];

        // 1. 推进时间（秒）
        renderData.animTimer += deltaTime;

        // 2. 正确计算当前帧索引：
        // (时间 * 帧率) -> 换算出累积播放的总帧数，转为 int 后再对总帧数取模
        int totalPlayedFrames = (int)(renderData.animTimer * renderData.animSpeed);
        int currentFrame = totalPlayedFrames % renderData.totalFrame;

        // 防止 animTimer 无限累加导致浮点数精度丢失，满一个完整循环后扣除时间
        float duration = renderData.totalFrame / renderData.animSpeed;
        if (renderData.animTimer >= duration)
        {
            renderData.animTimer %= duration;
        }

        renderDatas[index] = renderData; // 写回

        // 4. 输出帧索引供 Shader 采样
        frameIndices[index] = currentFrame;
    }
}

public struct MoveJob : IJobParallelFor
{
    public float deltaTime;
    [NativeDisableParallelForRestriction]
    public NativeArray<EnemyTransformData> positionsDatas;
    public NativeArray<Matrix4x4> matrices;
    public SpatialHashGrid hashGrid;

    public void Execute(int index)
    {
        //移动
        // ------------------------------------------------------------
        // 1. 移动逻辑 (朝着 targetPos 移动)
        // ------------------------------------------------------------
        float2 dir = positionsDatas[index].targetPos - positionsDatas[index].position;
        float distSq = math.lengthsq(dir);

        // 如果距离大于极小值，进行移动
        if (distSq > 0.001f)
        {
            float2 normDir = math.normalize(dir);

            var data = positionsDatas[index];
            data.position += normDir * positionsDatas[index].velocity * deltaTime;
            positionsDatas[index] = data;
        }

        // ------------------------------------------------------------
        // 2. 转向逻辑 (根据移动方向确定 Scale.x 的正负)
        // ------------------------------------------------------------
        // 如果朝左移动 (dir.x < 0)，Scale.x 设为 -1；朝右设为 1
        float scaleX = (dir.x < 0) ? -1.0f : 1.0f;
        Vector3 scale = new Vector3(scaleX, 1.0f, 1.0f);

        hashGrid.ResolveEnemySeparationForSingle(index,  ref positionsDatas, hashGrid,0.2f);
        
        // 3. 构建 2D 变换矩阵
        matrices[index] = Matrix4x4.TRS((Vector2)positionsDatas[index].position, Quaternion.identity, scale);
    }
}

public class SpineDODRenderManager : MonoBehaviour
{
    public const int MAX_ENEMIES = 1000;

    [Header("Render Assets")]
    public Material spineArrayMaterial;  // 使用上面写的 Shader 的材质
    public float animFPS = 12f;          // 动画播放帧率

    private Mesh _quadMesh;              // 原生 Quad 网格
    private NativeArray<EnemyRenderData2D> _renderDatas;
    private int _gridSize = 15;
    
    private Vector3 _currentTargetPos; // 保存当前目标点
    private NativeArray<EnemyTransformData> _transformDatas;
    private SpatialHashGrid _hashGrid;
    
    //战斗数据
    private NativeArray<EnemyCombatData> _combatDatas;
    
    private NativeArray<Matrix4x4> _nativeMatrices;
    private NativeArray<float> _nativeFrameIndices;

    private Matrix4x4[] _managedMatrices;
    private float[] _managedFrameIndices;
    private MaterialPropertyBlock _propertyBlock;

    private int _enemyCound = 800;

    private static readonly int FrameIndexPropertyID = Shader.PropertyToID("_FrameIndex");
    
    // ========================================================================
    // 单例模式实现
    // ========================================================================
    public static SpineDODRenderManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        
        // 创建默认 1x1 Quad Mesh (也可直接从 Unity 资源拖入)
        GameObject tempQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        _quadMesh = tempQuad.GetComponent<MeshFilter>().sharedMesh;
        Destroy(tempQuad);

        // 分配 Native 内存
        _transformDatas = new NativeArray<EnemyTransformData>(MAX_ENEMIES, Allocator.Persistent);
        _renderDatas = new NativeArray<EnemyRenderData2D>(MAX_ENEMIES, Allocator.Persistent);
        _combatDatas = new NativeArray<EnemyCombatData>(MAX_ENEMIES, Allocator.Persistent);
        _nativeMatrices = new NativeArray<Matrix4x4>(MAX_ENEMIES, Allocator.Persistent);
        _nativeFrameIndices = new NativeArray<float>(MAX_ENEMIES, Allocator.Persistent);

        _managedMatrices = new Matrix4x4[MAX_ENEMIES];
        _managedFrameIndices = new float[MAX_ENEMIES];
        _propertyBlock = new MaterialPropertyBlock();

        Texture2DArray texArray = spineArrayMaterial.GetTexture("_MainTexArray") as Texture2DArray;
        
        for (int i = 0; i < _enemyCound; i++)
        {
            var data = _transformDatas[i];
            data.position = new float2(UnityEngine.Random.Range(-_gridSize, _gridSize), UnityEngine.Random.Range(-_gridSize, _gridSize));
            data.velocity = UnityEngine.Random.Range(0.1f, 0.3f);
            _transformDatas[i] = data;
            
            _renderDatas[i] = new EnemyRenderData2D
            {
                animTimer = UnityEngine.Random.Range(0f, 1f),
                animSpeed = animFPS,
                totalFrame = texArray.depth
            };

            var hp = UnityEngine.Random.Range(5f, 20f);
            _combatDatas[i] = new EnemyCombatData
            {
                maxHp = hp,
                hp = hp,
            };
        }
        
        _hashGrid.Initialize(_enemyCound, 3f, 15, 15);
    }

    private void Update()
    {
        var ball = new ReflectBall();
        // 1. 检测鼠标左键点击或按住
        if (Input.GetMouseButton(0)) 
        {
            // 将鼠标屏幕坐标转化为世界坐标
            _currentTargetPos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            
            for (int i = 0; i < _enemyCound; i++)
            {
                var data = _transformDatas[i];
                data.targetPos = (Vector2)_currentTargetPos;
                _transformDatas[i] = data;
            }
        }
        
        // 1. 调度 Burst 多线程计算帧号和矩阵
        var job = new Build2DInstanceJob
        {
            deltaTime = Time.deltaTime,
            renderDatas = _renderDatas,
            frameIndices = _nativeFrameIndices
        };
        JobHandle renderHandle = job.Schedule(_enemyCound, 64);
        
        var moveJob = new MoveJob()
        {
            deltaTime = Time.deltaTime,
            positionsDatas = _transformDatas,
            matrices = _nativeMatrices,
            hashGrid =  _hashGrid
        };

        _hashGrid.ClearAndBuildGrid(ref _hashGrid, _transformDatas, _enemyCound);
        
        JobHandle moveHandle = moveJob.Schedule(_enemyCound, 64, renderHandle);
        moveHandle.Complete();

        // 2. 内存块极速 Copy 到托管数组
        NativeArray<Matrix4x4>.Copy(_nativeMatrices, _managedMatrices, _enemyCound);
        NativeArray<float>.Copy(_nativeFrameIndices, _managedFrameIndices, _enemyCound);

        // 3. 设置帧索引数组并一次性批量绘制！
        _propertyBlock.SetFloatArray(FrameIndexPropertyID, _managedFrameIndices);
        
        Graphics.DrawMeshInstanced(
            _quadMesh,
            0,
            spineArrayMaterial,
            _managedMatrices,
            _enemyCound,
            _propertyBlock
        );
    }
    
    //碰撞检测
    /// <summary>
    /// 零 GC 碰撞检测：直接将结果填入传入的 results 列表
    /// </summary>
    public void CheckCollision(Vector2 pos, float radius, List<int> results)
    {
        results.Clear(); // 必须清空，复用内存

        // 1. 计算当前所在的网格坐标 (注意：需修正为 worldMin 偏移)
        int cellX = Mathf.Clamp(Mathf.FloorToInt((pos.x + _hashGrid.gridWidth) / _hashGrid.cellSize), 0, _hashGrid.gridWidth - 1);
        int cellY = Mathf.Clamp(Mathf.FloorToInt((pos.y + _hashGrid.gridHeight) / _hashGrid.cellSize), 0, _hashGrid.gridHeight - 1);

        float minDistSq = radius * radius;

        // 2. 遍历周围 3x3 的网格格子
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                int targetCellX = cellX + dx;
                int targetCellY = cellY + dy;

                if (targetCellX < 0 || targetCellX >= _hashGrid.gridWidth ||
                    targetCellY < 0 || targetCellY >= _hashGrid.gridHeight) continue;

                int cellIndex = targetCellX + targetCellY * _hashGrid.gridWidth;
                int enemyIndex = _hashGrid.cellHeaders[cellIndex];

                while (enemyIndex != -1)
                {
                    // 确保只查询当前存活的敌人
                    if (enemyIndex < _enemyCound)
                    {
                        Vector2 enemyPos = _transformDatas[enemyIndex].position;
                        float distSq = (pos - enemyPos).sqrMagnitude;

                        // 发生碰撞，收集 enemyIndex
                        if (distSq <= minDistSq)
                        {
                            results.Add(enemyIndex);
                        }
                    }

                    enemyIndex = _hashGrid.nextEnemyIndices[enemyIndex];
                }
            }
        }
    }
    
    /// <summary>
    /// 粗筛：利用 DDA 算法高效获取线段 p1->p2 穿过的所有 Spatial Grid 网格索引
    /// </summary>
    public void GetGridCellsOnLine(Vector2 p1, Vector2 p2, HashSet<int> resultCells)
    {
        // 转换为相对于 worldMin 的网格坐标
        float cs = _hashGrid.cellSize;
        Vector2 temp = new Vector2(_hashGrid.gridWidth, _hashGrid.gridHeight);
        Vector2 startLocal = p1 + temp;
        Vector2 endLocal = p2 + temp;

        int x = Mathf.Clamp(Mathf.FloorToInt(startLocal.x / cs), 0, _hashGrid.gridWidth - 1);
        int y = Mathf.Clamp(Mathf.FloorToInt(startLocal.y / cs), 0, _hashGrid.gridHeight - 1);

        int endX = Mathf.Clamp(Mathf.FloorToInt(endLocal.x / cs), 0, _hashGrid.gridWidth - 1);
        int endY = Mathf.Clamp(Mathf.FloorToInt(endLocal.y / cs), 0, _hashGrid.gridHeight - 1);

        resultCells.Add(x + y * _hashGrid.gridWidth);

        int dx = (int)Mathf.Sign(endLocal.x - startLocal.x);
        int dy = (int)Mathf.Sign(endLocal.y - startLocal.y);

        float deltaX = dx != 0 ? Mathf.Abs(cs / (p2.x - p1.x)) : float.MaxValue;
        float deltaY = dy != 0 ? Mathf.Abs(cs / (p2.y - p1.y)) : float.MaxValue;

        float maxX = dx > 0 ? ((x + 1) * cs - startLocal.x) / (p2.x - p1.x) : (x * cs - startLocal.x) / (p2.x - p1.x);
        float maxY = dy > 0 ? ((y + 1) * cs - startLocal.y) / (p2.y - p1.y) : (y * cs - startLocal.y) / (p2.y - p1.y);

        if (dx == 0) maxX = float.MaxValue;
        if (dy == 0) maxY = float.MaxValue;

        // DDA 步进步进
        while (x != endX || y != endY)
        {
            if (maxX < maxY)
            {
                maxX += deltaX;
                x += dx;
            }
            else
            {
                maxY += deltaY;
                y += dy;
            }

            if (x >= 0 && x < _hashGrid.gridWidth && y >= 0 && y < _hashGrid.gridHeight)
            {
                resultCells.Add(x + y * _hashGrid.gridWidth);
            }
            else
            {
                break;
            }
        }
    }

    public void GetGridEnemyOnLine(Vector2 p1, Vector2 p2, float radius, int gridIndex, HashSet<int> resultEnemies)
    {
        // 1. 安全校验：如果格子索引无效，直接返回
        if (gridIndex < 0 || gridIndex >= _hashGrid.cellHeaders.Length) return;

        // 2. 获取该网格格子的头节点敌人索引
        int enemyIndex = _hashGrid.cellHeaders[gridIndex];

        float radiusSq = radius * radius;

        // 3. 遍历当前格子中的敌人链表
        while (enemyIndex != -1)
        {
            // 边界防护：确保敌人索引在当前存活数量范围内
            if (enemyIndex < _enemyCound)
            {
                Vector2 enemyPos = _transformDatas[enemyIndex].position;

                // 4. 精确检测：敌人坐标到线段 p1 -> p2 的最短平方距离是否 <= radiusSq
                if (IsPointNearSegment(enemyPos, p1, p2, radiusSq))
                {
                    resultEnemies.Add(enemyIndex);
                }
            }

            // 沿单链表检索下一个敌人
            enemyIndex = _hashGrid.nextEnemyIndices[enemyIndex];
        }
    }

    /// <summary>
    /// 数学辅助函数：判断点 point 到线段 lineStart -> lineEnd 的最短平方距离是否在 maxDistSq 范围内
    /// </summary>
    private bool IsPointNearSegment(Vector2 point, Vector2 lineStart, Vector2 lineEnd, float maxDistSq)
    {
        Vector2 line = lineEnd - lineStart;
        float lenSq = line.sqrMagnitude;

        // 如果线段退化为一个点
        if (lenSq < 0.0001f)
        {
            return (point - lineStart).sqrMagnitude <= maxDistSq;
        }

        // 计算点在向量上的投影比例 t，并限制在 [0, 1] 保证投影在有限线段上
        float t = Mathf.Clamp01(Vector2.Dot(point - lineStart, line) / lenSq);
        Vector2 projection = lineStart + t * line;

        return (point - projection).sqrMagnitude <= maxDistSq;
    }

    public void OnHitEnemy(int index, float damage)
    {
        // 1. 安全校验：如果索引超出了当前存活范围，直接返回
        if (index < 0 || index >= _enemyCound) return;

        // 2. 扣减血量并写回 NativeArray
        var data = _combatDatas[index];
        data.hp -= damage;
        _combatDatas[index] = data;

        // 3. 检查死亡条件
        if (data.hp <= 0f)
        {
            RemoveEnemyAtSwapBack(index);
        }
    }

    /// <summary>
    /// 使用 O(1) 的 Swap-Back 机制移除指定索引的敌人数据
    /// </summary>
    private void RemoveEnemyAtSwapBack(int index)
    {
        int lastIndex = _enemyCound - 1;

        // 如果要移除的恰好不是最后一个元素，则将最后一个元素覆盖到当前位置
        if (index != lastIndex)
        {
            _combatDatas[index] = _combatDatas[lastIndex];
            _transformDatas[index] = _transformDatas[lastIndex];
            _renderDatas[index] = _renderDatas[lastIndex];

            // 如果你还有其他平行的 NativeArray，也需要在这里一并 Swap-Back：
            // _nativeMatrices[index] = _nativeMatrices[lastIndex];
            // _nativeFrameIndices[index] = _nativeFrameIndices[lastIndex];
        }

        // 存活数量减 1（相当于丢弃了末尾项，没有任何内存移动开销）
        _enemyCound--;

        // (可选) 播放死亡特效或音效
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
        if (_transformDatas.IsCreated) _transformDatas.Dispose();
        if (_renderDatas.IsCreated) _renderDatas.Dispose();
        if (_nativeMatrices.IsCreated) _nativeMatrices.Dispose();
        if (_nativeFrameIndices.IsCreated) _nativeFrameIndices.Dispose();
    }
}