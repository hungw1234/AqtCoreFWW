-- Comprehensive permission seed for all GOV modules
-- Run: sqlcmd -S localhost -U sa -P 'YourStrong!Passw0rd' -d AqtCoreFW -C -i seed-permissions.sql
-- Idempotent: deletes existing and re-seeds

DELETE FROM [identity].RolePermissions;
DELETE FROM [identity].Permissions;

INSERT INTO [identity].Permissions (Id, Code, Name, Description, ModuleCode, ResourceCode, ActionCode, IsSensitive) VALUES
-- IDENTITY module (23 perms)
(NEWID(), 'IDENTITY.USER.VIEW',         N'Xem người dùng',              NULL, 'IDENTITY', 'USER',       'VIEW',    0),
(NEWID(), 'IDENTITY.USER.CREATE',       N'Tạo người dùng',              NULL, 'IDENTITY', 'USER',       'CREATE',  0),
(NEWID(), 'IDENTITY.USER.UPDATE',       N'Cập nhật người dùng',         NULL, 'IDENTITY', 'USER',       'UPDATE',  0),
(NEWID(), 'IDENTITY.USER.DELETE',       N'Xóa người dùng',              NULL, 'IDENTITY', 'USER',       'DELETE',  1),
(NEWID(), 'IDENTITY.ROLE.VIEW',         N'Xem vai trò',                 NULL, 'IDENTITY', 'ROLE',       'VIEW',    0),
(NEWID(), 'IDENTITY.ROLE.MANAGE',       N'Quản lý vai trò',             NULL, 'IDENTITY', 'ROLE',       'MANAGE',  1),
(NEWID(), 'IDENTITY.PERM.ASSIGN',       N'Gán quyền cho vai trò',       NULL, 'IDENTITY', 'PERM',       'ASSIGN',  1),
(NEWID(), 'IDENTITY.GROUP.VIEW',        N'Xem nhóm người dùng',         NULL, 'IDENTITY', 'GROUP',      'VIEW',    0),
(NEWID(), 'IDENTITY.GROUP.MANAGE',      N'Quản lý nhóm',                NULL, 'IDENTITY', 'GROUP',      'MANAGE',  0),
(NEWID(), 'IDENTITY.DELEG.VIEW',        N'Xem ủy quyền',                NULL, 'IDENTITY', 'DELEG',      'VIEW',    0),
(NEWID(), 'IDENTITY.DELEG.MANAGE',      N'Quản lý ủy quyền',            NULL, 'IDENTITY', 'DELEG',      'MANAGE',  1),
(NEWID(), 'IDENTITY.ABAC.VIEW',         N'Xem quy tắc ABAC',            NULL, 'IDENTITY', 'ABAC',       'VIEW',    0),
(NEWID(), 'IDENTITY.ABAC.MANAGE',       N'Quản lý ABAC',                NULL, 'IDENTITY', 'ABAC',       'MANAGE',  1),
(NEWID(), 'IDENTITY.SOD.VIEW',          N'Xem quy tắc SoD',             NULL, 'IDENTITY', 'SOD',        'VIEW',    0),
(NEWID(), 'IDENTITY.SOD.MANAGE',        N'Quản lý SoD',                 NULL, 'IDENTITY', 'SOD',        'MANAGE',  1),
(NEWID(), 'IDENTITY.SESSION.VIEW',      N'Xem phiên đăng nhập',         NULL, 'IDENTITY', 'SESSION',    'VIEW',    0),
(NEWID(), 'IDENTITY.SESSION.REVOKE',    N'Thu hồi phiên',               NULL, 'IDENTITY', 'SESSION',    'REVOKE',  1),
(NEWID(), 'IDENTITY.MENU.VIEW',         N'Xem menu',                    NULL, 'IDENTITY', 'MENU',       'VIEW',    0),
(NEWID(), 'IDENTITY.MENU.MANAGE',       N'Quản lý menu',                NULL, 'IDENTITY', 'MENU',       'MANAGE',  0),
(NEWID(), 'IDENTITY.POLICY.VIEW',       N'Xem chính sách bảo mật',      NULL, 'IDENTITY', 'POLICY',     'VIEW',    0),
(NEWID(), 'IDENTITY.POLICY.MANAGE',     N'Quản lý chính sách',          NULL, 'IDENTITY', 'POLICY',     'MANAGE',  1),
(NEWID(), 'IDENTITY.CONSENT.VIEW',      N'Xem đồng ý PDPL',            NULL, 'IDENTITY', 'CONSENT',    'VIEW',    0),
(NEWID(), 'IDENTITY.DATASCOPE.MANAGE',  N'Quản lý phạm vi dữ liệu',    NULL, 'IDENTITY', 'DATASCOPE',  'MANAGE',  1),
-- FILES module (8 perms)
(NEWID(), 'FILES.FILE.VIEW',            N'Xem tài liệu',                NULL, 'FILES',    'FILE',       'VIEW',    0),
(NEWID(), 'FILES.FILE.UPLOAD',          N'Tải lên tài liệu',            NULL, 'FILES',    'FILE',       'UPLOAD',  0),
(NEWID(), 'FILES.FILE.DELETE',          N'Xóa tài liệu',                NULL, 'FILES',    'FILE',       'DELETE',  1),
(NEWID(), 'FILES.FILE.DOWNLOAD',        N'Tải xuống tài liệu',          NULL, 'FILES',    'FILE',       'DOWNLOAD',0),
(NEWID(), 'FILES.TEMPLATE.VIEW',        N'Xem mẫu tài liệu',           NULL, 'FILES',    'TEMPLATE',   'VIEW',    0),
(NEWID(), 'FILES.TEMPLATE.MANAGE',      N'Quản lý mẫu tài liệu',       NULL, 'FILES',    'TEMPLATE',   'MANAGE',  0),
(NEWID(), 'FILES.RETENTION.VIEW',       N'Xem chính sách lưu trữ',      NULL, 'FILES',    'RETENTION',  'VIEW',    0),
(NEWID(), 'FILES.RETENTION.MANAGE',     N'Quản lý chính sách lưu trữ',  NULL, 'FILES',    'RETENTION',  'MANAGE',  1),
-- AUDIT module (7 perms)
(NEWID(), 'AUDIT.LOG.VIEW',             N'Xem nhật ký kiểm toán',       NULL, 'AUDIT',    'LOG',        'VIEW',    0),
(NEWID(), 'AUDIT.LOG.EXPORT',           N'Xuất nhật ký',                NULL, 'AUDIT',    'LOG',        'EXPORT',  0),
(NEWID(), 'AUDIT.LOGIN.VIEW',           N'Xem lịch sử đăng nhập',       NULL, 'AUDIT',    'LOGIN',      'VIEW',    0),
(NEWID(), 'AUDIT.RTBF.VIEW',            N'Xem yêu cầu RTBF',           NULL, 'AUDIT',    'RTBF',       'VIEW',    0),
(NEWID(), 'AUDIT.RTBF.EXECUTE',         N'Thực thi xóa dữ liệu RTBF',  NULL, 'AUDIT',    'RTBF',       'EXECUTE', 1),
(NEWID(), 'AUDIT.INCIDENT.VIEW',        N'Xem sự cố bảo mật',          NULL, 'AUDIT',    'INCIDENT',   'VIEW',    0),
(NEWID(), 'AUDIT.INCIDENT.MANAGE',      N'Quản lý sự cố bảo mật',      NULL, 'AUDIT',    'INCIDENT',   'MANAGE',  1),
-- WORKFLOW module (6 perms)
(NEWID(), 'WORKFLOW.DEF.VIEW',           N'Xem quy trình',              NULL, 'WORKFLOW', 'DEF',        'VIEW',    0),
(NEWID(), 'WORKFLOW.DEF.MANAGE',         N'Quản lý quy trình',          NULL, 'WORKFLOW', 'DEF',        'MANAGE',  0),
(NEWID(), 'WORKFLOW.TASK.VIEW',          N'Xem công việc',              NULL, 'WORKFLOW', 'TASK',       'VIEW',    0),
(NEWID(), 'WORKFLOW.TASK.PROCESS',       N'Xử lý công việc',            NULL, 'WORKFLOW', 'TASK',       'PROCESS', 0),
(NEWID(), 'WORKFLOW.INSTANCE.VIEW',      N'Xem tiến trình quy trình',   NULL, 'WORKFLOW', 'INSTANCE',   'VIEW',    0),
(NEWID(), 'WORKFLOW.ASSIGN.MANAGE',      N'Phân công công việc',         NULL, 'WORKFLOW', 'ASSIGN',     'MANAGE',  0),
-- FORMS module (6 perms)
(NEWID(), 'FORMS.TEMPLATE.VIEW',         N'Xem biểu mẫu',              NULL, 'FORMS',    'TEMPLATE',   'VIEW',    0),
(NEWID(), 'FORMS.TEMPLATE.MANAGE',       N'Quản lý biểu mẫu',          NULL, 'FORMS',    'TEMPLATE',   'MANAGE',  0),
(NEWID(), 'FORMS.SUBMIT.VIEW',           N'Xem dữ liệu gửi',           NULL, 'FORMS',    'SUBMIT',     'VIEW',    0),
(NEWID(), 'FORMS.SUBMIT.CREATE',         N'Gửi biểu mẫu',              NULL, 'FORMS',    'SUBMIT',     'CREATE',  0),
(NEWID(), 'FORMS.SUBMIT.EXPORT',         N'Xuất dữ liệu biểu mẫu',     NULL, 'FORMS',    'SUBMIT',     'EXPORT',  0),
(NEWID(), 'FORMS.VIEW.MANAGE',           N'Quản lý view biểu mẫu',     NULL, 'FORMS',    'VIEW',       'MANAGE',  0),
-- NOTIFICATIONS module (4 perms)
(NEWID(), 'NOTIF.MSG.VIEW',              N'Xem thông báo',              NULL, 'NOTIF',    'MSG',        'VIEW',    0),
(NEWID(), 'NOTIF.MSG.SEND',              N'Gửi thông báo',              NULL, 'NOTIF',    'MSG',        'SEND',    0),
(NEWID(), 'NOTIF.TEMPLATE.VIEW',         N'Xem mẫu thông báo',         NULL, 'NOTIF',    'TEMPLATE',   'VIEW',    0),
(NEWID(), 'NOTIF.TEMPLATE.MANAGE',       N'Quản lý mẫu thông báo',     NULL, 'NOTIF',    'TEMPLATE',   'MANAGE',  0),
-- ORGANIZATION module (4 perms)
(NEWID(), 'ORG.UNIT.VIEW',               N'Xem đơn vị',                NULL, 'ORG',      'UNIT',       'VIEW',    0),
(NEWID(), 'ORG.UNIT.MANAGE',             N'Quản lý đơn vị',            NULL, 'ORG',      'UNIT',       'MANAGE',  0),
(NEWID(), 'ORG.STAFF.VIEW',              N'Xem nhân sự',               NULL, 'ORG',      'STAFF',      'VIEW',    0),
(NEWID(), 'ORG.STAFF.MANAGE',            N'Quản lý phân công nhân sự',  NULL, 'ORG',      'STAFF',      'MANAGE',  0),
-- MASTERDATA module (5 perms)
(NEWID(), 'MASTER.DATA.VIEW',            N'Xem dữ liệu danh mục',      NULL, 'MASTER',   'DATA',       'VIEW',    0),
(NEWID(), 'MASTER.DATA.MANAGE',          N'Quản lý danh mục',           NULL, 'MASTER',   'DATA',       'MANAGE',  0),
(NEWID(), 'MASTER.DICT.VIEW',            N'Xem từ điển dữ liệu',       NULL, 'MASTER',   'DICT',       'VIEW',    0),
(NEWID(), 'MASTER.DICT.MANAGE',          N'Quản lý từ điển',            NULL, 'MASTER',   'DICT',       'MANAGE',  0),
(NEWID(), 'MASTER.EXTMAP.MANAGE',        N'Quản lý ánh xạ ngoài',      NULL, 'MASTER',   'EXTMAP',     'MANAGE',  0),
-- AI module (5 perms)
(NEWID(), 'AI.EXTRACT.USE',              N'Sử dụng AI trích xuất',      NULL, 'AI',       'EXTRACT',    'USE',     0),
(NEWID(), 'AI.MODEL.VIEW',               N'Xem cấu hình AI model',     NULL, 'AI',       'MODEL',      'VIEW',    0),
(NEWID(), 'AI.MODEL.MANAGE',             N'Quản lý AI model',           NULL, 'AI',       'MODEL',      'MANAGE',  1),
(NEWID(), 'AI.PROMPT.VIEW',              N'Xem mẫu prompt',            NULL, 'AI',       'PROMPT',     'VIEW',    0),
(NEWID(), 'AI.PROMPT.MANAGE',            N'Quản lý mẫu prompt',        NULL, 'AI',       'PROMPT',     'MANAGE',  0),
-- COLLABORATION module (3 perms)
(NEWID(), 'COLLAB.CONV.VIEW',            N'Xem hội thoại',             NULL, 'COLLAB',   'CONV',       'VIEW',    0),
(NEWID(), 'COLLAB.CONV.CREATE',          N'Tạo hội thoại',             NULL, 'COLLAB',   'CONV',       'CREATE',  0),
(NEWID(), 'COLLAB.MSG.SEND',             N'Gửi tin nhắn',              NULL, 'COLLAB',   'MSG',        'SEND',    0),
-- INTEGRATION module (5 perms)
(NEWID(), 'INTEG.CONTRACT.VIEW',         N'Xem hợp đồng tích hợp',    NULL, 'INTEG',    'CONTRACT',   'VIEW',    0),
(NEWID(), 'INTEG.CONTRACT.MANAGE',       N'Quản lý hợp đồng',         NULL, 'INTEG',    'CONTRACT',   'MANAGE',  0),
(NEWID(), 'INTEG.PARTNER.VIEW',          N'Xem đối tác',              NULL, 'INTEG',    'PARTNER',    'VIEW',    0),
(NEWID(), 'INTEG.PARTNER.MANAGE',        N'Quản lý đối tác',          NULL, 'INTEG',    'PARTNER',    'MANAGE',  0),
(NEWID(), 'INTEG.MSGLOG.VIEW',           N'Xem log tích hợp',         NULL, 'INTEG',    'MSGLOG',     'VIEW',    0),
-- REPORTING module (5 perms)
(NEWID(), 'REPORT.DASH.VIEW',            N'Xem dashboard',            NULL, 'REPORT',   'DASH',       'VIEW',    0),
(NEWID(), 'REPORT.DASH.MANAGE',          N'Quản lý dashboard',        NULL, 'REPORT',   'DASH',       'MANAGE',  0),
(NEWID(), 'REPORT.REPORT.VIEW',          N'Xem báo cáo',              NULL, 'REPORT',   'REPORT',     'VIEW',    0),
(NEWID(), 'REPORT.REPORT.EXPORT',        N'Xuất báo cáo',             NULL, 'REPORT',   'REPORT',     'EXPORT',  0),
(NEWID(), 'REPORT.WIDGET.MANAGE',        N'Quản lý widget',           NULL, 'REPORT',   'WIDGET',     'MANAGE',  0),
-- RULES module (5 perms)
(NEWID(), 'RULES.RULESET.VIEW',          N'Xem bộ quy tắc',           NULL, 'RULES',    'RULESET',    'VIEW',    0),
(NEWID(), 'RULES.RULESET.MANAGE',        N'Quản lý bộ quy tắc',       NULL, 'RULES',    'RULESET',    'MANAGE',  0),
(NEWID(), 'RULES.DTABLE.VIEW',           N'Xem bảng quyết định',      NULL, 'RULES',    'DTABLE',     'VIEW',    0),
(NEWID(), 'RULES.DTABLE.MANAGE',         N'Quản lý bảng quyết định',  NULL, 'RULES',    'DTABLE',     'MANAGE',  0),
(NEWID(), 'RULES.EVAL.USE',              N'Đánh giá quy tắc',         NULL, 'RULES',    'EVAL',       'USE',     0),
-- SEARCH module (2 perms)
(NEWID(), 'SEARCH.QUERY.USE',            N'Tìm kiếm',                 NULL, 'SEARCH',   'QUERY',      'USE',     0),
(NEWID(), 'SEARCH.SAVED.MANAGE',         N'Quản lý truy vấn đã lưu',  NULL, 'SEARCH',   'SAVED',      'MANAGE',  0),
-- SYSTEMPARAMS module (6 perms)
(NEWID(), 'SYSPARAM.PARAM.VIEW',         N'Xem tham số hệ thống',     NULL, 'SYSPARAM', 'PARAM',      'VIEW',    0),
(NEWID(), 'SYSPARAM.PARAM.MANAGE',       N'Quản lý tham số',          NULL, 'SYSPARAM', 'PARAM',      'MANAGE',  1),
(NEWID(), 'SYSPARAM.FLAG.VIEW',          N'Xem feature flags',         NULL, 'SYSPARAM', 'FLAG',       'VIEW',    0),
(NEWID(), 'SYSPARAM.FLAG.MANAGE',        N'Quản lý feature flags',     NULL, 'SYSPARAM', 'FLAG',       'MANAGE',  1),
(NEWID(), 'SYSPARAM.ANNOUNCE.VIEW',      N'Xem thông báo hệ thống',   NULL, 'SYSPARAM', 'ANNOUNCE',   'VIEW',    0),
(NEWID(), 'SYSPARAM.ANNOUNCE.MANAGE',    N'Quản lý thông báo',         NULL, 'SYSPARAM', 'ANNOUNCE',   'MANAGE',  0),
-- SYSTEM ADMIN (2 perms)
(NEWID(), 'SYSTEM.CONFIG.VIEW',          N'Xem cấu hình hệ thống',    NULL, 'SYSTEM',   'CONFIG',     'VIEW',    0),
(NEWID(), 'SYSTEM.CONFIG.MANAGE',        N'Quản lý cấu hình',          NULL, 'SYSTEM',   'CONFIG',     'MANAGE',  1)
;

PRINT 'Seeded ' + CAST(@@ROWCOUNT AS VARCHAR) + ' permissions across all modules';
GO
