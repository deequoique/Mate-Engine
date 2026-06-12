# PetRemote Integration Notes

这次改动把原本 Electron 宠物端的远程控制链路，直接迁到了 `Mate-Engine` 源码里，目标是先完成计划里的阶段 1 和阶段 2 最小闭环：远程连接、命令分发、动作触发、音频播放、基础口型同步。

## 新增目录

- `Assets/PetRemote/Scripts/RemoteManager.cs`
  远程总入口。负责读取环境变量、建立连接、处理 socket 事件、响应 `pet:list-voices` / `pet:list-motions`、把命令交给分发器。

- `Assets/PetRemote/Scripts/SocketIoPollingClient.cs`
  一个最小可用的 Socket.IO polling 客户端实现。当前项目里没有现成 Unity Socket.IO 客户端，所以这里先按现有 server 协议做了兼容层，覆盖 `pet:join`、`pet:command`、ack 回调和房间事件。

- `Assets/PetRemote/Scripts/CommandDispatcher.cs`
  远程命令分发器。负责把 `expression` / `animation` / `say_audio` / `say_tts` / `relocate` 映射到 Mate-Engine 现有能力。

- `Assets/PetRemote/Scripts/VoicePlayer.cs`
  远程音频播放器。用 `UnityWebRequestMultimedia.GetAudioClip` 下载并播放音频，同时每帧取频谱驱动 `aa` 嘴型。

- `Assets/PetRemote/Scripts/VoiceLibrary.cs`
  扫描 `Assets/StreamingAssets/voices` 下的音频文件，返回给 A 端作为预录台词清单。当前返回的是本机 `file:///...` URL，A 端只负责转发给 Unity 端回放。

- `Assets/PetRemote/Scripts/MotionLibrary.cs`
  动作清单桥接层。优先读取 `AvatarDanceHandler` 当前实际可用动作，再结合 manifest 补 label / loop 信息。

- `Assets/PetRemote/Scripts/RemoteCommandModels.cs`
  远程协议模型：命令、ack、房间状态、动作元信息。

- `Assets/PetRemote/Scripts/PetRemoteBootstrap.cs`
  自动启动入口。场景加载后会自动创建 `PetRemote` 常驻对象，不要求手工往场景里拖预制体。

- `Assets/Resources/motions/manifest.json`
  从现有项目的动作 manifest 复制过来的 Unity 资源版本，供 `MotionLibrary` 读取。

- `Assets/StreamingAssets/voices/README.md`
  说明预录台词应该放在哪里，以及推荐的 `head_` / `body_` / `tail_` / `idle_` 命名方式。

## 修改的现有文件

- `Assets/MATE ENGINE - Scripts/AvatarHandlers/UniversalBlendshapes.cs`
  新增远程友好的表情入口：
  - `SetEmotion(string, float)`
  - `ClearEmotion(string)`
  - `SetPresetValue(string, float)`

  同时补了自定义表情名直通能力，专门兼容远程协议里的 `surprised`、`aa` 等逻辑表情名。

- `Assets/MATE ENGINE - Scripts/AvatarHandlers/AvatarDancePlayer.cs`
  给 `AvatarDanceHandler` 增加了远程调用入口：
  - `GetAvailableMotions()`
  - `TryPlayByName(string)`
  - `TryPlayFirst()`

  这样远程层不需要直接碰内部 `entries` 结构。

- `Assets/MATE ENGINE - Scripts/AvatarHandlers/AvatarWindowHandler.cs`
  增加了 `RelocateToCorner(string cornerName, int margin = 24)`，直接复用现有 Win32 窗口能力实现四角定位。

- `Assets/MATE ENGINE - Scripts/VRMLoader/VRMLoader.cs`
  在模型加载完成、恢复默认模型、重置模型时，都会调用 `PetRemoteBootstrap.NotifyAvatarLoaded(...)`，让远程层重新绑定：
  - `UniversalBlendshapes`
  - `AvatarDanceHandler`
  - `AvatarWindowHandler`

## 现在的运行方式

- 默认连接地址：`http://localhost:3030`
- 默认房间密钥：`change-me`
- 支持环境变量覆盖：
  - `PET_SERVER_URL`
  - `SERVER_URL`
  - `PET_ROOM_SECRET`
  - `ROOM_SECRET`

## 目前范围

已经完成：

- 远程连接与 `pet:join`
- `pet:command` 分发
- `pet:list-voices` ack
- `pet:list-motions` ack
- `expression`
- `animation`
- `say_audio`
- `say_tts`
- `relocate`
- 音频频谱驱动嘴型

还没做：

- 点击分区反应
- AI 聊天气泡
- WebRTC
- 正式第三方 Socket.IO Unity 包替换

## 额外说明

当前远程连接层是为了尽快对齐现有 `server/` 与 `web/` 协议而写的最小 polling 版本。后续如果要切换成正式 Unity Socket.IO 包，优先替换 `Assets/PetRemote/Scripts/SocketIoPollingClient.cs` 即可，其它上层接口可以保持不动。
