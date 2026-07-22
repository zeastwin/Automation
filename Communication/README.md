# 通讯运行时源码导航

`Communication/` 是 TCP 与串口通讯枢纽。`CommunicationHub.cs` 包含通道、帧解码、接收分发和事务等待；`CommunicationModels.cs` 保存跨层使用的通讯配置模型。

当前只有两个高度内聚文件，保持扁平结构。只有出现新的独立协议实现时才增加子目录，不为现有类型增加包装层。
