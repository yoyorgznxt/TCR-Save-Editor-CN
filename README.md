# 《全面冲突：抵抗》存档编辑器
《全面冲突：抵抗》(Total Conflict: Resistance) 的存档编辑器
需要 .Net10 运行环境

## qwen3.6翻译，forked from [MaximumLeet/TCR-Save-Editor](https://github.com/MaximumLeet/TCR-Save-Editor/)

# 使用说明
### **1. 操作 > 打开存档**
* 程序会自动打开 TCR 存档目录。
	* 如果无法找到该目录，您需要手动查找存档位置。
* 存档文件通常命名为 "TCR_v85_0.sav"
* "_0" 是您的存档槽位，例如我在第 24 槽的存档会命名为 "TCR_v85_24.sav"

### **2. 编辑数值**
* 从 v1.0.0 开始，强烈建议在向城市添加或移除资源时使用 resource_table.py GUI，因为您需要在那里找到资源 ID。

### **3. 操作 > 保存更改**
* 更改将写入存档文件。
* 如果遇到任何问题
	* 请通过 Discord 或 Steam 联系我。
	* 会显示一条消息说明具体问题。
* 如果没有问题，会显示一条消息确认文件已验证可用。

### **4. 操作 > 安装到游戏**
* 编辑后的存档将被重命名
* 如果原始存档的备份不存在
	* 将在存档目录中创建一个名为 "TCRSEBackup" 的新文件夹
	* 原始存档文件将存储在此处，以防编辑后的存档出现任何问题。

# 已知限制
* 尚不支持编辑军队/营编制。目前仅支持编辑固定数值选项，如资源、人口和阵营范围的属性（政治、权威、稳定、独裁点数）。

# 未来可能添加的功能
* 军队/营编制编辑（添加单位、武器等）
* 欢迎提出任何建议！

# 注意事项
* 运行 .exe 文件时可能会看到 Windows Smart Screen 警告。这是预期行为。这是因为可执行文件未签名且尚无下载记录。这是基于文件的新颖度/识别度的启发式检测，并非发现文件本身有任何问题。
* 查看 [VirusTotal](https://www.virustotal.com/gui/file-analysis/ODM0NjJiMWM4MTc1NzFjYWVhNDA4OTA3Zjk5MmJjMzI6MTc4NjM2ODc3Ng==) 扫描或从源代码构建以确认此构建的完整性。
