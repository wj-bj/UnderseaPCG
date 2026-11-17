# UnderseaPCG

用 Unity 做的水下场景：GPU 程序化生成地形和植被，GPU 模拟鱼群。用户用中文交流，请用中文回复。

## 环境

- Unity **6000.4.0f1**，渲染管线 **URP 17.4.0**（2026-11 从 Built-in 迁移过来），色彩空间 **Linear**。
- Git 仓库根目录是上一级 `E:\Github\Unity\UnderseaPCG`（`README.md` 在那里），Unity 工程在 `UnderseaPCG/` 子目录，也就是本文件所在目录。
- 工程装了 `com.unity.pipeline`，可以用 `unity` CLI 驱动正在运行的 Editor（查状态、执行 C#、截图、进出 Play 模式）。做任何场景或资源改动前先 `unity status`，不要手改 `.unity` / `.prefab` / `.asset` 的 YAML。
- URP 资源在 `Assets/Settings/`：`URP_Asset.asset` 指定给 Graphics 和全部 6 个 Quality 档位，开了 Depth Texture、Opaque Texture、HDR、4x MSAA。它挂两个 Renderer：索引 0 是 `URP_Renderer`（旧场景用），索引 1 是 `Assets/UnderseaV2/Settings/URP_Renderer_Underwater.asset`（新场景用，带水下全屏 pass）。

## 两个场景

| 场景 | 路径 | 状态 |
|---|---|---|
| 旧场景 | `Assets/Scenes/undersea.unity` | 原始版本，迁移到 URP 后能正常渲染，但没有雾和水下后处理。保留作对照，不再开发。 |
| 新场景 | `Assets/UnderseaV2/Scenes/UnderseaV2.unity` | 当前开发的场景，从旧场景复制后逐步替换各系统。 |

`Assets/Scenes/undersea_unsaved_backup.unity` 是切换场景前旧场景未保存状态的备份，用户确认不需要后可以删。

新场景的所有新代码和资源都在 `Assets/UnderseaV2/` 下。原则是**不破坏旧场景**：改共用代码时加默认关闭的开关，只在新场景里打开。

## 新场景的系统（Assets/UnderseaV2）

场景根物体：`Underwater Environment`、`Water Surface V2`、`Vegetation System`、`Fish School System`、`Global Volume`，以及从旧场景继承的 `Mesh Generator`、`Noise Density`、`SceneManager`（带 `MapGenerator`）、`Agent Generator`、`UICanvas` 等。

### 水体：全局参数、雾、焦散、光柱

- `Scripts/UnderwaterEnvironment.cs`：唯一的水体参数来源，每帧把水面高度、吸收系数、散射色、太阳方向、焦散和光柱参数写成 `_UW_*` 全局 shader 变量。调参都在这个组件的 Inspector 上。
- `Shaders/UnderwaterCommon.hlsl`：公共函数。`UW_WaterTerms` / `UW_ApplyWater` 算沿视线的吸收和散射（闭式积分），`UW_Caustics` 是程序化焦散，`UW_ShaftPattern` 是光柱用的低频图案（已归一化到平均值 1）。任何需要自己上雾的 shader（比如透明物体）都应 include 它。
- `Shaders/UnderwaterFog.shader` + `Scripts/UnderwaterVolumeFeature.cs`：自定义 Renderer Feature，在不透明物体和天空之后、透明物体之前做一次全屏 pass。它用深度和法线纹理重建世界坐标，叠加焦散（只在朝太阳且不在阴影里的面上），再沿视线步进 20 次采样太阳阴影贴图做光柱。pass 里调用了 `UseAllGlobalTextures(true)` 来拿阴影贴图。
- 相机要用 Renderer 索引 1 才有这些效果（`UniversalAdditionalCameraData.SetRenderer(1)`）。

### 水面

- `Shaders/WaterSurfaceV2.shader`：按“从水下往上看”写的。斯涅尔窗内透出天空和太阳，窗外全反射（反射色用 `UW_ApplyWater` 算，和雾一致）。波纹是程序化噪声，不用贴图。在 Transparent 队列绘制，所以它自己调用 `UW_ApplyWater` 上雾。
- `Scripts/WaterSurfaceFollow.cs`：让 600 米的水面平面跟着当前渲染的相机水平移动；shader 用世界坐标，所以不会滑动。
- 旧的 `WaterSurface` 物体还在场景里，只关了渲染（保留碰撞体），并且**阴影投射必须是 Off**，见下面的坑。

### 地形

- 流程：`MapGenerator`（CPU，2D 元胞自动机，生成 `densityMap` 和 `reefMap`）→ `NoiseDensity.compute`（3D 密度场）→ 遮罩和 3D 元胞自动机 → `MarchingCubes.compute` → `Assets/Scripts/Generator/MeshGenerator.cs` 读回建 Mesh。由 `Assets/Scripts/Others/SceneManager.cs` 串联，Play 开始时生成。
- 新场景设置：单区块、边长 64 米、每轴 64 个采样点、`gradientNormals` 和 `smoothMask` 都打开。
- `Shaders/TerrainV2.shader`（材质 `Materials/TerrainV2.mat`）：平地铺沙、陡坡岩石，按坡度加噪声过渡；岩石三平面映射，沙地俯视投影；朝上的岩石面带藻类色。贴图来自 `Assets/Mesh/sea-rock-free/textures/Ground/`。

### 植被

- `Shaders/VegetationScatter.compute`：以地形三角形为单位在 GPU 上撒点，按坡度、高度、离水面距离、成片噪声、礁区掩码筛选，输出到 Append Buffer。
- `Scripts/VegetationSystem.cs`：每层一次 `DrawMeshInstancedIndirect`，没有 GameObject。订阅 `MeshGenerator.MeshesUpdated`，地形重新生成时自动重铺。层的参数在组件 Inspector 上。
- `Scripts/VegetationMeshes.cs`：代码生成的海草丛和海带网格。
- `Shaders/Vegetation.shader`：顶点摆动、双面光照、透光、可选透明裁剪。材质是 `Materials/Vegetation_*.mat`。
- 现有 18 层约 1.8 万个实例：海草丛、海带、2 种海草、2 种扇形珊瑚、12 种实体珊瑚（网格取自 `Assets/Lib3rd/_Corals/Corals.FBX` 和 `Seaweeds.FBX`，FBX 里共有 33 个珊瑚网格可用）。
- 新场景里 `SceneManager.decorationGenerator` 被置空，旧的逐物体装饰生成不再运行。

### 鱼群

- `Shaders/FishSchool.compute`：四个 kernel——`ClearGrid`、`BuildGrid`、`Simulate`、`CopyDensity`。用 3 米的均匀网格找邻居（每格最多 24 条，`CELL_CAPACITY` 要和 C# 里的 `CellCapacity` 一致），读地形密度场做避障，双缓冲乒乓。
- `Scripts/FishSchoolSystem.cs`：鱼种配置（`FishSpecies` 列表）、缓冲管理和绘制。只在 Play 模式下模拟。`FollowTarget` 是一个用异步读回跟踪某条鱼的 Transform，UI 的 Fish 相机视角用它。`Diagnose()` 会读回全部鱼做统计，只用于调试。
- `Shaders/FishV2.shader`：同一个网格（`Assets/Lib3rd/Fish/Models/fish_modified.fbx`，工程里唯一的鱼模型）按鱼种改体型比例、背腹配色、条纹和反光。
- 现有 6 个鱼种共 2244 条：沙丁鱼 1400、蓝色乌尾鮗 320、黄色刺尾鱼 260、条纹礁鱼 220、梭鱼 40、礁鲨 4（捕食者）。
- 旧的 `Fish.prefab`（`FishFlocking`，128 条）已从新场景的 `AgentGenerator.agents` 里移除。美人鱼（新场景用 `Mermaid_V2` 变体，见下）、潜水员、宝物仍由 `AgentGenerator` 生成，用 NPBehave 行为树和 UnityMovementAI 的 steering。

### 美人鱼

- `Scripts/MermaidBehaviourV2.cs`：新场景的美人鱼行为，挂在预制体变体 `Prefabs/Mermaid_V2.prefab` 上（原 `Assets/Prefabs/Mermaid_prefab.prefab` 没动，旧场景仍用它）。它在 `Awake` 里把 `MermaidMoveUnit.externalControl` 置为 true 并禁用 `MermaidBTController`，自己用 NPBehave 行为树加一个小的运动控制器接管。`MermaidMoveUnit` 必须留在物体上：UI 的 Mermaid 视角和宝物的触发器都靠它找美人鱼。
- 行为优先级：躲鲨鱼（`Flee`，之后回头张望）> 追宝物（追到后转圈庆祝，15 秒内不再追）> 空闲活动。空闲活动按 `weights` 随机挑，不连续重复：巡游、悬停休息、翻滚嬉戏（桶滚 + 空翻）、螺旋上浮到水面再俯冲、绕鱼群共游、探望并绕潜水员、贴海底滑行。
- 模型只有两段动画（`idle_fix` 和 `Foward`，由 Animator 的 `IsSeek` 切换）。俯仰、转弯侧倾、桶滚、空翻、悬停浮动都是程序化的，加在带 Animator 的子物体 `Mermaid_mesh` 上；根物体只绕 Y 轴转，朝向是 `transform.right`。**idle 动画本身已近乎直立**，悬停时不要再叠加大的 `posePitch`。
- 鲨鱼和鱼群的位置来自 `FishSchoolSystem` 的异步快照（`EnableSnapshot()` 后每 0.4 秒读回一次全部鱼）：`NearestPredator`、`FindSchool`、`TrackSchool`。
- 调试：`Report()` 返回当前状态，`Force(Activity)` 立刻切到某个空闲活动（会屏蔽追宝物和躲鲨鱼 20 秒）。

## 搭建脚本

`Assets/UnderseaV2/Editor/UnderseaV2Setup.cs` 里的静态方法可以重复执行，用来创建或修复新场景的配置，都返回一段文字报告：

`SetupRenderer()` → `CreateScene()` → `SetupTerrain(pointsPerAxis)` → `SetupWater()` → `SetupVegetation()` → `SetupFish()` → `SetupMermaid()`，另有 `Regenerate()` 在编辑模式下重建地图、地形和植被。

**注意**：`SetupVegetation()` 和 `SetupFish()` 会用代码里的默认值覆盖场景里的层和鱼种列表。如果用户在 Inspector 里调过参数，重新执行会丢失这些调整；要改默认值就改这个脚本。

汇报素材用的两个工具（只在 Play 模式下生效，不改场景文件）：`Assets/UnderseaV2/Editor/ShowcaseStages.cs` 的 `SetStage(0–6)` 按阶段逐层开关各系统、`MakeCameras()` 建固定机位，用来拍同机位的前后对比图；`ShowcaseRecorder.cs` 用 Unity Recorder（`com.unity.recorder` 5.1.8）从主相机录 mp4。素材输出在 `Screenshots/showcase/`。

`Assets/Editor/UrpMigration.cs` 是 URP 迁移时的一次性脚本，`Assets/Settings/MaterialSnapshot_BuiltIn.txt` 是迁移前的材质记录，都可以删。

## 对共用代码的改动

这些文件两个场景都在用，改动都保持旧场景行为不变：

- `Assets/Lib3rd/MarchingCube/Scripts/Compute/MarchingCubes.compute`：三角形结构体多了三个法线（由密度梯度算出）。C# 端的缓冲 stride 是 18 个 float，两边必须一致。
- `Assets/Scripts/Generator/MeshGenerator.cs`：新增 `gradientNormals`、`smoothMask` 两个开关（默认关），`MeshesUpdated` 事件，`PointsBuffer` 只读访问器（密度场，只在 Play 模式有效）。
- `Assets/Scripts/Agent/MermaidMoveUnit.cs`：新增 `externalControl` 开关（默认关），打开后它不再驱动运动。
- `Assets/Scripts/Others/UIController.cs`：Fish 相机视角找不到 `FishMoveUnit` 时改用 `FishSchoolSystem.FollowTarget`。
- `Assets/Lib3rd/MarchingCube/Scripts/Underwater/FogEffectCommandBuffer.cs`：在 SRP 下直接跳过。
- URP 迁移时原地重写成 URP HLSL 的 shader（shader 名没变）：`Custom/Terrain`、`Custom/FishAnimation`、`Custome/water2`（有两份相同的文件，材质引用的是 `Assets/Lib3rd/Fish/Shaders/Underwater/WaterSurface2.shader`）。
- 珊瑚的 3 个 Amplify 材质转成了 URP Lit。

## 踩过的坑

- **水面不能投射阴影**：水面平面开着阴影投射会把整个海底罩在阴影里，焦散和光柱全部消失，地形也变暗。
- **密度值不是距离**：地形遮罩会把密度按 0.05–1 缩放，所以不能直接拿密度和等值面比较来判断“离岩石多远”，要用 `(isoLevel - density) / |梯度|` 估算。
- **需要 DepthNormals pass**：全屏 pass 申请了法线纹理，URP 会改用 DepthNormals pass 来生成深度图。任何新写的不透明 shader 都要有 `DepthNormals` pass，否则它不写深度，雾和焦散在它身上会算错。
- **URP 17 只有 Render Graph**：自定义 pass 要实现 `RecordRenderGraph`；需要阴影贴图这类全局纹理时调用 `UseAllGlobalTextures(true)`。
- **Procedural instancing 的写法**：`#pragma instancing_options procedural:setup`，在 `setup()` 里把实例数据写进 `static` 变量，顶点函数里手动变换到世界空间。`DrawMeshInstancedIndirect` 放在 `RenderPipelineManager.beginCameraRendering` 回调里并传入相机，这样编辑模式、Scene 视图和截图都能画出来。
- **编辑模式下旧地形是灰色或黑色**：`ColourGenerator` 的渐变贴图是运行时生成的，刚打开旧场景时还没有，属于原有行为。
- **已经没有 Surface Shader 和 OnRenderImage**：这些在 URP 下不工作。工程里还留着几个没移植的旧 shader（`Custom/Sea World`、`PeerPlay/NoiseGround`、`ASE/CausticGround`、`Flocking/*`、`Custom/WaterSurface`），当前没有物体在用，用到会显示洋红。
- **光柱图案的平均值**：`UW_ShaftPattern` 里除以 0.31 是为了让图案平均值为 1，改图案系数时要重新算，否则开关光柱会改变水体整体亮度。

## 用 CLI 验证画面的做法

- 执行 C#：`unity command script eval --project-path <工程路径> --code '...'`。代码是语句块，不能写 `using`，类型要写全名，不能用扩展方法。
- 截图：`unity command capture game_view --source camera --camera <相机名> --output-path <路径>`。按名字找不到 `hideFlags = DontSave` 的相机。
- 进 Play 后等地形生成（约 10 秒）再截图。主相机由 `CameraController` 控制，想从固定机位看就临时建一个 `enabled = false` 的相机并 `SetRenderer(1)`。
- 鱼和植被没有 Transform，要定位它们用 `FishSchoolSystem.SampleFish` / `SpeciesCentre` 或 `VegetationSystem.Report()`。
- 截图存在工程根目录的 `Screenshots/`（没有被 git 忽略）。

## 已知局限和待办

- 植被没有视锥剔除和 LOD，所有实例每帧都提交；只有海带投射阴影。
- 鱼的避障不完美，偶尔有鱼贴着或轻微穿进岩石；避障只支持单区块地形。
- 鲨鱼只能感知 3 米内的猎物（受网格格子大小限制）。
- 鱼支持躲避带 `Player` 标签的物体，但场景里没有物体带这个标签，所以没有生效。
- 光柱步进用了抖动，没有时间滤波，近看有细网纹。
- 水面从上往下看的分支只写了没验证；斯涅尔窗边缘偏硬。
- 岩石贴图是卡通风格的碎石图，换写实贴图会更好。
- 植被的摆动只从代码上确认，没有逐帧验证过观感。
- Play 模式帧率约 50–64（Editor 内，含 2244 条鱼和 1.8 万株植被）。

## 版本控制状态

写这份文档时，URP 迁移和整个 `Assets/UnderseaV2/` 都**还没有提交**，分支是 `main`。工作区里还有 Unity 6 升级带来的改动（TextMesh Pro 资源、`ProjectSettings` 等）。提交前先和用户确认范围，只在用户要求时提交。
