# 创建新服务

在 Moirai Framework 中创建一个新的游戏服务。

## 参数
- $SERVICE_NAME: 服务名称（如：QuestService、ShopService）

## 任务

1. **分析需求**：理解要创建的服务功能

2. **创建目录结构**：
   ```
   Assets/MoiraiFramework/Runtime/Services/{SERVICE_NAME}/
   ├── {SERVICE_NAME}.cs          # 服务主类
   ├── {SERVICE_NAME}Data.cs      # 服务数据（如需要）
   └── Components/               # 服务组件（如需要）
   ```

3. **生成服务代码**：
   - 继承 `Service` 基类
   - 实现 `OnInit()`、`OnShow()`、`OnHide()`、`OnUpdate()` 等生命周期方法
   - 使用 `[Service]` 特性标记
   - 遵循框架的命名规范

4. **服务模板**：
   ```csharp
   using Moirai.Runtime.Core.Attributes;

   namespace Moirai.Runtime.Services.{SERVICE_NAME}
   {
       [Service]
       public class {SERVICE_NAME} : Service
       {
           protected override void OnInit()
           {
               base.OnInit();
               // 初始化逻辑
           }

           protected override void OnShow()
           {
               base.OnShow();
               // 显示时逻辑
           }

           protected override void OnHide()
           {
               base.OnHide();
               // 隐藏时逻辑
           }

           protected override void OnUpdate()
           {
               base.OnUpdate();
               // 每帧更新逻辑
           }

           protected override void OnDestroy()
           {
               base.OnDestroy();
               // 销毁清理逻辑
           }
       }
   }
   ```

5. **注册服务**：在 `GameApp.cs` 或相关配置中注册新服务

6. **创建 Editor 支持**（如需要）：
   ```
   Assets/MoiraiFramework/Editor/Services/{SERVICE_NAME}/
   ```

## 注意事项
- 遵循框架的高内聚低耦合原则
- 使用 UniTask 进行异步操作
- 使用框架的事件系统进行服务间通信
- 确保内存管理正确（对象池、资源释放）

请告诉我您要创建什么服务，我会帮您生成完整的代码。
