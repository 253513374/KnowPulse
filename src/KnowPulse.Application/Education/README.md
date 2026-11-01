# 历史学校记录的内部授权校验

学校教育页面、公开应用服务、后台任务及内嵌小程序已按 [ADR 0055](../../../docs/adr/0055-retire-school-workflows.md) 退出。当前教师出题和学生答题不要求学校成员、任教或在班关系。

本目录仅保留当前代码仍使用的内部守卫：

- `EducationCurriculumAccessGuard`：核对已有课程快照的账号、关系和版本。
- `EducationMaterialScope`：核对历史材料来源及指定版本授权。
- `QuestionCurriculumAccessGuard`：核对已有题目和冻结评分的课程依据。

这些守卫不提供学校管理 API。历史实体、EF 映射与迁移继续保留，不因退出产品流程而删除资料、任务、答卷或成绩。
