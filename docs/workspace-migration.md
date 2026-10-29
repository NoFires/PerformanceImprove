# 工作区迁移

2026-10-21，按用户要求将完整工作区从文档目录迁至 `E:\Program\Development\WrathPerformance`。

包含 `.git`、源码、测试、反编译输出、运行记录、安装包和备份。迁移前后逐文件 SHA-256 核对一致，详见 `analysis/workspace-migration.json` 和 `E:\Program\Caches\WorkspaceMigration\WrathPerformance-copy-verification.json`。

当前 MCP 工作目录、源码默认根、安装脚本、技能及文档链接已切换。MCP 配置遵循 [OpenAI 官方配置说明](https://learn.chatgpt.com/docs/extend/mcp?surface=cli)。已运行的 MCP 进程需要重新连接以采用新根；新启动连接使用 E 盘。

历史 JSON、日志和旧包中的 C 盘路径保留原文，不更改测量证据；路径后缀对应 E 盘新根。当前安装入口的备份路径已更新。游戏内 Mod 和游戏存档不属于本次搬迁对象。

迁移完成：旧目录只保留 README.md 和 AGENTS.md 指引，旧原文副本归档在 `E:\Program\Caches\WorkspaceMigration\OriginalWorkspaceBeforeRelocation`。新根编译通过，零警告零错误；新 MCP 连接五项调用及越界拒绝通过。已安装 Mod 与发行包哈希保持不变。
