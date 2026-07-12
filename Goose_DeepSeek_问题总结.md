# Goose + DeepSeek 问题总结

## 一、问题现象

在当前电脑上，Goose 是直接复制到以下目录使用的：

```text
D:\AutomationTools\Goose
```

调用 DeepSeek 时出现：

```text
Request failed: Bad request (400):
The reasoning_content in the thinking mode must be passed back to the API.
```

## 二、初步原因

Goose 程序目录和 Goose 用户配置是分开的。通过 PowerShell 执行 `goose configure` 保存的配置通常位于：

```text
%APPDATA%\Block\goose\config\
```

重点文件包括：

```text
config.yaml
custom_providers\*.json
```

因此，只复制 `D:\AutomationTools\Goose`，不会自动复制旧电脑上的 Provider、模型和 Goose 配置。

API Key 也不一定存放在 Goose 程序目录中。当前 Automation 项目使用本机用户级加密文件保存 Provider Key：

```text
%APPDATA%\Automation\AiProviderSecrets.dat
```

该文件与 Windows 当前用户绑定，不能直接假设从另一台电脑复制后仍然可用。

## 三、两个错误需要区分

### 1. Provider 缺失错误

日志中曾出现：

```text
Unknown provider 'deepseek'
```

这表示 Goose 没有找到对应 Provider 注册，通常与以下问题有关：

- `config.yaml` 没有复制；
- `custom_providers` 目录或 Provider JSON 缺失；
- Provider 名称不一致，例如 `deepseek` 和 `custom_deepseek`；
- Goose 版本或配置目录不同。

### 2. `reasoning_content` 400 错误

当前机器上的配置已经被 Automation 自动生成，当前配置为：

```yaml
GOOSE_PROVIDER: custom_deepseek
GOOSE_MODEL: deepseek-v4-pro
```

Provider JSON 当前关键字段为：

```json
{
  "engine": "openai",
  "base_url": "https://api.deepseek.com/chat/completions"
}
```

这说明当前请求已经找到 Provider 并发送到 DeepSeek，现阶段的 400 不再是简单的配置文件缺失问题。

DeepSeek 思考模式在执行工具调用后，要求后续请求完整带回上一轮的 `reasoning_content`。如果 Goose 的 OpenAI 兼容适配器只带回普通 `content`，没有带回 `reasoning_content`，DeepSeek 就会返回这个 400 错误。

参考：

- [DeepSeek 思考模式官方说明](https://api-docs.deepseek.com/guides/thinking_mode)

## 四、到正常电脑需要对照的内容

请在正常电脑上对照以下信息：

### Goose 版本和实际路径

```powershell
goose --version
where.exe goose
```

### Goose 用户配置

```text
%APPDATA%\Block\goose\config\config.yaml
%APPDATA%\Block\goose\config\custom_providers\
```

重点比较：

```yaml
GOOSE_PROVIDER:
GOOSE_MODEL:
```

以及 Provider JSON 中的：

```json
"engine": "...",
"base_url": "...",
"models": [...]
```

### Automation 配置

```text
bin\Debug\Config\GooseConfig.json
```

重点比较：

```json
{
  "GooseExecutablePath": "...",
  "WorkingDirectory": "...",
  "Provider": "...",
  "Model": "..."
}
```

### API Key 配置方式

不要把 API Key 直接写入日志或提交到 Git。只需确认两台电脑使用的 Key 配置方式一致：

- Goose 自身环境变量或配置；
- Automation 的 `%APPDATA%\Automation\AiProviderSecrets.dat`；
- 或其他本机密钥存储方式。

如果 Key 缺失或无效，通常会出现认证错误（例如 401/403），而不是当前这个 `reasoning_content` 400。

## 五、当前最可能的结论

问题可能经历了两个阶段：

1. 复制 Goose 后没有复制用户配置，导致最初出现 `Unknown provider`；
2. Automation 自动生成了 `custom_deepseek`，Provider 可以使用，但其 `engine: openai` 适配链路没有正确处理 DeepSeek 思考模式的 `reasoning_content`，于是出现当前 400。

因此，正常电脑上最有价值的对照项是：

- Goose 版本是否相同；
- Provider 的 `engine` 是否也是 `openai`；
- `base_url` 是否相同；
- 是否启用了 thinking 模式；
- 是否使用了不同的 Goose Provider 适配方式；
- 是否通过工具调用连续进行多轮对话。

