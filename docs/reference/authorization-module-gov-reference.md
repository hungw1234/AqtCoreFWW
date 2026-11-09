# Tài liệu thiết kế module phân quyền cho hệ thống hành chính nhà nước

> **AqtCoreFW Reference** — Nguồn: internal baseline (2026-03-13)
> **Relevance:** Phase 04 Identity — DataScopeType, Permission Code Convention, Effective Permission Viewer, Delegation partial-scope
> **Gap analysis:** `plans/reports/` session 2026-03-13 analysis

**Phiên bản:** 1.0
**Ngày tạo:** 2026-03-13  
**Phạm vi:** Tài liệu tổng hợp nghiên cứu và đề xuất baseline cho module phân quyền dùng trong các hệ thống hành chính, dịch vụ công, quản lý hồ sơ, báo cáo, danh mục và quản trị hệ thống.

---

## 1. Mục tiêu tài liệu

Tài liệu này tổng hợp đầy đủ phần nghiên cứu và triển khai theo ba hướng:

1. Thiết kế chi tiết module phân quyền cho hệ thống hành chính nhà nước.
2. Đề xuất schema database chuẩn cho module phân quyền.
3. Viết use case, màn hình và luồng API cho module phân quyền.

Tài liệu hướng tới việc dùng làm baseline cho BA, SA, DBA, DEV, QC và PM khi phân tích, thiết kế, triển khai và kiểm thử module phân quyền.

---

## 2. Nguyên tắc thiết kế tổng thể

### 2.1. Bài toán thực sự của phân quyền

Một module phân quyền đúng nghĩa phải trả lời đồng thời bốn câu hỏi:

1. **Ai** được thao tác.
2. **Được thao tác gì**.
3. **Được thao tác trên dữ liệu nào**.
4. **Ai đã cấp quyền / thay đổi quyền / sử dụng quyền**.

Vì vậy không thể chỉ dừng ở mức gán role cho user.

### 2.2. Mô hình khuyến nghị

Baseline khuyến nghị:

- **RBAC** làm lõi.
- **Data Scope** để giới hạn phạm vi dữ liệu.
- **Policy Rule** để kiểm soát theo điều kiện nghiệp vụ.
- **Delegation** để hỗ trợ ủy quyền tạm thời.
- **Audit Trail** để theo dõi thay đổi và sử dụng quyền.

### 2.3. Các nguyên tắc nền tảng

- Mặc định là **deny by default**.
- FE chỉ hỗ trợ UX; **BE/API mới là nơi quyết định cuối cùng**.
- Kiểm soát quyền ở cả **menu, action, API và dữ liệu**.
- Hỗ trợ mô hình tổ chức nhiều cấp: Bộ, Cục/Vụ, Sở, Phòng, đơn vị trực thuộc.
- Tách bạch **Authentication** và **Authorization**.
- Ưu tiên quản trị quyền qua **role/group**, hạn chế cấp trực tiếp cho từng user.
- Mọi thay đổi quyền phải có **log before/after**.

---

## 3. Thiết kế chi tiết module phân quyền cho hệ thống hành chính nhà nước

## 3.1. Mục tiêu nghiệp vụ

Module phân quyền phải hỗ trợ các nhu cầu sau:

- Quản lý người dùng nội bộ và người dùng nghiệp vụ.
- Gán người dùng vào một hoặc nhiều đơn vị.
- Gán một hoặc nhiều vai trò cho người dùng hoặc nhóm.
- Kiểm soát truy cập tới menu, chức năng, button, API.
- Kiểm soát quyền dữ liệu theo đơn vị, cây đơn vị, hồ sơ do mình tạo, hồ sơ được phân công, lĩnh vực, trạng thái.
- Kiểm soát theo workflow state: ví dụ chỉ được phê duyệt khi hồ sơ ở trạng thái chờ duyệt.
- Hỗ trợ ủy quyền khi người có thẩm quyền nghỉ phép, vắng mặt.
- Ghi nhận đầy đủ lịch sử cấp quyền, thu hồi quyền, thay đổi phạm vi dữ liệu, thay đổi ủy quyền.

## 3.2. Các lớp quyền

### 3.2.1. Quyền chức năng

Trả lời câu hỏi: người dùng có được dùng chức năng này hay không.

Ví dụ:

- xem danh sách hồ sơ
- thêm hồ sơ
- sửa hồ sơ
- xóa hồ sơ
- trình ký
- phê duyệt
- xuất Excel
- cấu hình hệ thống
- quản trị người dùng
- gán role
- xem audit log

### 3.2.2. Quyền dữ liệu

Trả lời câu hỏi: người dùng được thao tác trên tập dữ liệu nào.

Ví dụ:

- chỉ dữ liệu do chính người dùng tạo
- dữ liệu được phân công xử lý
- dữ liệu của phòng ban mình
- dữ liệu của đơn vị mình
- dữ liệu của đơn vị mình và toàn bộ đơn vị con
- dữ liệu toàn tỉnh / toàn bộ / toàn hệ thống
- dữ liệu của danh sách đơn vị được cấp riêng
- dữ liệu theo lĩnh vực, loại hồ sơ, thủ tục, trạng thái, giai đoạn thời gian

### 3.2.3. Quyền theo điều kiện nghiệp vụ

Trả lời câu hỏi: cùng một quyền chức năng nhưng có cần ràng buộc thêm điều kiện không.

Ví dụ:

- được sửa hồ sơ khi trạng thái là `DRAFT`
- được trình ký khi trạng thái là `CHO_TRINH`
- được duyệt khi trạng thái là `CHO_DUYET`
- được phát hành khi hồ sơ đã ký số
- được xóa khi hồ sơ chưa liên thông / chưa phát sinh thanh toán

### 3.2.4. Quyền theo thời gian hoặc thời hạn hiệu lực

Ví dụ:

- quyền ủy quyền chỉ có hiệu lực từ ngày A đến ngày B
- quyền tạm cấp phục vụ hỗ trợ vận hành chỉ dùng trong 2 ngày
- tài khoản thử nghiệm chỉ hiệu lực trong giai đoạn UAT

## 3.3. Mô hình vai trò

### 3.3.1. Nguyên tắc thiết kế role

Role phải theo **nghiệp vụ**, không theo tên người.

Nên dùng:

- `CHUYEN_VIEN_TIEP_NHAN`
- `CHUYEN_VIEN_XU_LY`
- `LANH_DAO_PHONG`
- `LANH_DAO_DON_VI`
- `VAN_THU`
- `QUAN_TRI_DON_VI`
- `QUAN_TRI_HE_THONG`
- `CAN_BO_BAO_CAO`

Không nên dùng:

- `ROLE_ANH_A`
- `ROLE_HA_NOI_01`
- `ROLE_XU_LY_DAC_BIET_CUA_B`

### 3.3.2. Vai trò hệ thống và vai trò nghiệp vụ

Nên tách hai nhóm:

**Vai trò quản trị hệ thống**
- quản lý user, group, role, permission
- cấu hình SSO, tham số hệ thống, danh mục nền
- xem log hệ thống
- không mặc định có toàn quyền dữ liệu nghiệp vụ

**Vai trò quản trị nghiệp vụ**
- cấu hình quy trình, biểu mẫu, danh mục nghiệp vụ
- phân công xử lý hồ sơ
- xem dữ liệu trong phạm vi đơn vị hoặc toàn hệ thống tùy cấp

## 3.4. Group và direct assignment

### 3.4.1. Group

Group dùng để gom người dùng cùng tính chất.

Ví dụ:

- nhóm Chuyên viên tiếp nhận
- nhóm Lãnh đạo phòng
- nhóm Cán bộ tổng hợp
- nhóm Văn thư
- nhóm Quản trị đơn vị

Lợi ích:

- giảm số lần gán trực tiếp
- dễ xử lý khi nhân sự thay đổi
- dễ áp chuẩn theo mô hình tổ chức

### 3.4.2. Direct assignment

Cho phép nhưng hạn chế. Chỉ dùng khi:

- có ngoại lệ đặc biệt
- cần cấp quyền tạm thời
- cần override nhỏ mà không muốn tạo thêm role mới

Direct assignment phải có:
- lý do cấp
- thời hạn hiệu lực
- người phê duyệt / người cấp
- log thay đổi

## 3.5. Phạm vi dữ liệu (Data Scope)

### 3.5.1. Các loại scope nền tảng

- `SELF`: bản ghi do user tạo
- `ASSIGNED`: bản ghi được phân công cho user
- `ORG_UNIT`: bản ghi thuộc đơn vị user
- `ORG_TREE`: bản ghi thuộc đơn vị user và đơn vị con
- `CUSTOM_LIST`: bản ghi theo danh sách đơn vị được gán riêng
- `ALL`: toàn bộ dữ liệu
- `BY_FIELD`: theo lĩnh vực / loại thủ tục / trạng thái / địa bàn / nguồn dữ liệu

### 3.5.2. Mô hình scope khuyến nghị

Data scope nên tách thành hai tầng:

1. **Loại scope chuẩn**
2. **Giá trị scope cụ thể**

Ví dụ:
- loại scope = `CUSTOM_LIST`
- giá trị scope = danh sách `OrgUnitId`

hoặc:
- loại scope = `BY_FIELD`
- giá trị scope = `FieldName = LinhVuc`, `FieldValue = DatDai`

### 3.5.3. Nguyên tắc xử lý scope

- scope phải được áp ở tầng query hoặc service, không lọc muộn ở FE
- phải chuẩn hóa cách kết hợp nhiều scope
- cần quy tắc rõ khi user có nhiều role, nhiều scope

Khuyến nghị:
- hợp các quyền cho phép
- hợp các scope được cấp
- nhưng vẫn bị ràng buộc bởi policy rule và trạng thái nghiệp vụ

## 3.6. Policy Rule

Policy rule dùng cho những điều kiện không thể biểu diễn gọn bằng role/scope.

Ví dụ:
- quyền `APPROVE` chỉ có hiệu lực khi `WorkflowState = CHO_DUYET`
- quyền `DELETE` chỉ có hiệu lực khi `IsLinked = 0`
- quyền `EXPORT` chỉ có hiệu lực khi hồ sơ không nằm trong danh sách mật

Rule nên hỗ trợ:
- tên rule
- resource áp dụng
- action áp dụng
- biểu thức điều kiện
- mức ưu tiên
- cho phép / từ chối
- log khi bị từ chối

## 3.7. Delegation

Delegation dùng để ủy quyền tạm thời.

Tối thiểu cần có:
- người ủy quyền
- người được ủy quyền
- danh sách quyền hoặc role được ủy quyền
- phạm vi dữ liệu được ủy quyền
- thời gian bắt đầu / kết thúc
- trạng thái hiệu lực
- lý do ủy quyền

Audit phải thể hiện được:
- ai thực hiện thao tác
- thao tác đó dùng quyền gốc của ai
- thao tác diễn ra trong thời gian hiệu lực nào

## 3.8. Audit và nhật ký

Tối thiểu nên có bốn lớp log:

1. **Permission Change Log**: ai cấp gì cho ai, thay đổi trước/sau.
2. **Authorization Decision Log**: user gọi API nào, được phép hay bị từ chối, vì sao.
3. **Sensitive Action Log**: export, delete, approve, reset password, thay đổi cấu hình.
4. **Delegation Log**: thao tác phát sinh qua cơ chế ủy quyền.

## 3.9. Tích hợp SSO / AD / LDAP

Khuyến nghị kiến trúc:

- SSO/AD/LDAP lo xác thực.
- Hệ thống nghiệp vụ lo phân quyền.
- Có thể map group AD sang role nội bộ, nhưng không nên để toàn bộ logic nghiệp vụ phụ thuộc AD.

Nên đồng bộ các thuộc tính:
- mã người dùng
- họ tên
- email
- đơn vị chính
- chức danh
- trạng thái tài khoản
- danh sách group ngoài nếu có

## 3.10. SoD - Segregation of Duties

Module phân quyền cần hỗ trợ kiểm soát phân tách trách nhiệm.

Ví dụ:
- người cấp role quản trị không đồng thời là người duyệt cuối cùng quyền đó nếu quy trình yêu cầu phê duyệt
- người cấu hình workflow không mặc nhiên có quyền duyệt hồ sơ
- người vận hành hạ tầng không được xem dữ liệu nghiệp vụ mật nếu không có quyền nghiệp vụ

Có thể triển khai SoD ở mức:
- cảnh báo cấu hình xung đột
- chặn cấu hình xung đột
- yêu cầu phê duyệt hai bước với quyền nhạy cảm

## 3.11. Hiệu năng và cache

Các điểm cần tối ưu:
- cache permission theo user/session/token version
- cache menu tree
- cache role-permission
- invalidate cache khi thay đổi quyền
- query data scope phải có index phù hợp
- không tính lại toàn bộ effective permission cho từng request nếu không cần

---

## 4. Đề xuất schema database chuẩn cho module phân quyền

## 4.1. Danh sách bảng cốt lõi

### Nhóm người dùng và tổ chức
- `SEC_User`
- `SEC_Group`
- `SEC_UserGroup`
- `SEC_OrgUnit`
- `SEC_UserOrgUnit`

### Nhóm quyền
- `SEC_Role`
- `SEC_Permission`
- `SEC_RolePermission`
- `SEC_UserRole`
- `SEC_GroupRole`

### Tài nguyên truy cập
- `SEC_Menu`
- `SEC_RoleMenu`
- `SEC_ApiResource`
- `SEC_RoleApiResource`

### Phạm vi dữ liệu
- `SEC_DataScopeType`
- `SEC_RoleDataScope`
- `SEC_UserDataScopeOverride`

### Rule và ủy quyền
- `SEC_PolicyRule`
- `SEC_Delegation`

### Audit và log
- `SEC_AuditPermissionChange`
- `SEC_AccessDecisionLog`
- `SEC_SensitiveActionLog`

## 4.2. Quan hệ logic

- Một user có thể thuộc nhiều group.
- Một user có thể được gán nhiều role.
- Một group có thể được gán nhiều role.
- Một role có nhiều permission.
- Một role có thể truy cập nhiều menu và nhiều API resource.
- Một role có thể có nhiều data scope.
- Một user có thể có override data scope.
- Một delegation có thể sinh thêm hiệu lực quyền tạm thời.

## 4.3. Định nghĩa các bảng chính

### 4.3.1. SEC_User
Thông tin tài khoản nội bộ.

Các trường đề xuất:
- `Id`
- `UserName`
- `DisplayName`
- `Email`
- `PhoneNumber`
- `EmployeeCode`
- `PrimaryOrgUnitId`
- `PositionName`
- `AuthSource`
- `IsActive`
- `LastLoginAt`
- `CreatedAt`
- `CreatedBy`
- `UpdatedAt`
- `UpdatedBy`

### 4.3.2. SEC_Group
Nhóm người dùng.

### 4.3.3. SEC_Role
Vai trò nghiệp vụ hoặc hệ thống.

Các trường đề xuất:
- `Code`
- `Name`
- `RoleType` (`SYSTEM`, `BUSINESS`)
- `Description`
- `IsActive`

### 4.3.4. SEC_Permission
Quyền chức năng.

Các trường đề xuất:
- `Code` theo chuẩn `MODULE.RESOURCE.ACTION`
- `Name`
- `ModuleCode`
- `ResourceCode`
- `ActionCode`
- `Description`
- `IsSensitive`

### 4.3.5. SEC_Menu
Cây menu giao diện.

### 4.3.6. SEC_ApiResource
Tài nguyên API.

Các trường đề xuất:
- `ModuleCode`
- `HttpMethod`
- `PathPattern`
- `ResourceCode`
- `ActionCode`
- `PermissionCode`

### 4.3.7. SEC_DataScopeType
Danh mục loại data scope chuẩn.

Ví dụ:
- `SELF`
- `ASSIGNED`
- `ORG_UNIT`
- `ORG_TREE`
- `CUSTOM_LIST`
- `ALL`
- `BY_FIELD`

### 4.3.8. SEC_RoleDataScope
Cấu hình scope cho role.

Các trường đề xuất:
- `RoleId`
- `DataScopeTypeId`
- `ScopeField`
- `ScopeValue`
- `Priority`
- `IsAllow`

### 4.3.9. SEC_UserDataScopeOverride
Override scope cho user.

### 4.3.10. SEC_PolicyRule
Rule điều kiện.

Các trường đề xuất:
- `Code`
- `Name`
- `ResourceCode`
- `ActionCode`
- `ConditionExpression`
- `Effect` (`ALLOW`, `DENY`)
- `Priority`
- `IsActive`

### 4.3.11. SEC_Delegation
Ủy quyền tạm thời.

Các trường đề xuất:
- `FromUserId`
- `ToUserId`
- `RoleId`
- `ScopeJson`
- `ValidFrom`
- `ValidTo`
- `Status`
- `Reason`

## 4.4. Quy tắc khóa và chỉ mục

### Khóa duy nhất
- `SEC_User(UserName)`
- `SEC_Group(Code)`
- `SEC_Role(Code)`
- `SEC_Permission(Code)`
- `SEC_DataScopeType(Code)`

### Unique mapping
- `SEC_UserGroup(UserId, GroupId)`
- `SEC_UserRole(UserId, RoleId, IsDirect)`
- `SEC_GroupRole(GroupId, RoleId)`
- `SEC_RolePermission(RoleId, PermissionId)`
- `SEC_RoleMenu(RoleId, MenuId)`
- `SEC_RoleApiResource(RoleId, ApiResourceId)`

### Chỉ mục khuyến nghị
- theo `PrimaryOrgUnitId`
- theo `PermissionCode`
- theo `RoleId`
- theo `UserId`
- theo `ValidFrom`, `ValidTo`
- theo `CreatedAt` cho log/audit

---

## 5. Use case + màn hình + luồng API

## 5.1. Danh sách use case chính

### UC-01. Quản lý người dùng
- tạo mới user nội bộ
- cập nhật user
- khóa/mở user
- gán user vào đơn vị
- xem quyền hiệu lực của user

### UC-02. Quản lý nhóm người dùng
- tạo nhóm
- sửa nhóm
- gán user vào nhóm
- gỡ user khỏi nhóm

### UC-03. Quản lý vai trò
- tạo role
- sửa role
- ngừng hiệu lực role
- gán permission cho role
- gán menu và API resource cho role

### UC-04. Gán role cho user/group
- gán trực tiếp role cho user
- gán role cho group
- thu hồi role
- ghi log thay đổi

### UC-05. Cấu hình data scope
- gán scope cho role
- gán override scope cho user
- khai báo danh sách đơn vị tùy biến
- thiết lập phạm vi theo field/lĩnh vực/trạng thái

### UC-06. Cấu hình policy rule
- tạo rule mới
- bật/tắt rule
- thay đổi thứ tự ưu tiên
- kiểm tra xung đột rule

### UC-07. Ủy quyền tạm thời
- tạo ủy quyền
- phê duyệt ủy quyền nếu có quy trình
- kết thúc / thu hồi ủy quyền
- tra cứu thao tác phát sinh từ ủy quyền

### UC-08. Tra cứu hiệu lực quyền
- nhập user
- xem role trực tiếp
- xem role từ group
- xem permission hiệu lực
- xem data scope hiệu lực
- xem delegation đang hoạt động

### UC-09. Nhật ký và kiểm tra
- xem lịch sử thay đổi quyền
- xem authorization decision log
- xem log thao tác nhạy cảm
- lọc theo user, thời gian, hành động, module

## 5.2. Màn hình đề xuất

### Màn hình 1: Danh sách người dùng
Các cột:
- UserName
- Họ tên
- Đơn vị chính
- Chức danh
- Trạng thái
- Nguồn xác thực
- Lần đăng nhập cuối
- Thao tác

### Màn hình 2: Chi tiết người dùng
Tabs:
- Thông tin cơ bản
- Đơn vị / chức danh
- Nhóm
- Vai trò trực tiếp
- Quyền hiệu lực
- Data scope hiệu lực
- Lịch sử thay đổi

### Màn hình 3: Danh sách nhóm
- Mã nhóm
- Tên nhóm
- Số user
- Trạng thái

### Màn hình 4: Danh sách role
- Mã role
- Tên role
- Loại role
- Trạng thái
- Số permission
- Số user/group đang sử dụng

### Màn hình 5: Chi tiết role
Tabs:
- Thông tin chung
- Permission
- Menu
- API resource
- Data scope
- User/Group đang dùng
- Lịch sử thay đổi

### Màn hình 6: Cấu hình data scope
Cho phép chọn:
- role hoặc user
- loại scope
- đơn vị áp dụng
- field áp dụng
- giá trị
- độ ưu tiên
- hiệu lực

### Màn hình 7: Danh sách policy rule
- Code
- Tên
- Resource
- Action
- Effect
- Priority
- Trạng thái

### Màn hình 8: Ủy quyền
- Người ủy quyền
- Người nhận ủy quyền
- Role/quyền được ủy quyền
- Thời gian hiệu lực
- Trạng thái
- Lý do

### Màn hình 9: Effective Permission Viewer
Đây là màn hình rất quan trọng.

Hiển thị:
- user
- đơn vị
- role trực tiếp
- role qua group
- permission tổng hợp
- data scope tổng hợp
- policy đang tác động
- delegation đang hiệu lực

### Màn hình 10: Audit Log
Bộ lọc:
- thời gian
- người thao tác
- user bị tác động
- module
- loại hành động
- loại log

## 5.3. Luồng API khuyến nghị

### Nhóm User
- `GET /api/security/users`
- `POST /api/security/users`
- `GET /api/security/users/{id}`
- `PUT /api/security/users/{id}`
- `POST /api/security/users/{id}/activate`
- `POST /api/security/users/{id}/deactivate`
- `GET /api/security/users/{id}/effective-permissions`

### Nhóm Group
- `GET /api/security/groups`
- `POST /api/security/groups`
- `PUT /api/security/groups/{id}`
- `POST /api/security/groups/{id}/members`
- `DELETE /api/security/groups/{id}/members/{userId}`

### Nhóm Role
- `GET /api/security/roles`
- `POST /api/security/roles`
- `PUT /api/security/roles/{id}`
- `POST /api/security/roles/{id}/permissions`
- `POST /api/security/roles/{id}/menus`
- `POST /api/security/roles/{id}/api-resources`

### Nhóm Assignment
- `POST /api/security/users/{id}/roles`
- `DELETE /api/security/users/{id}/roles/{roleId}`
- `POST /api/security/groups/{id}/roles`
- `DELETE /api/security/groups/{id}/roles/{roleId}`

### Nhóm Data Scope
- `GET /api/security/data-scope-types`
- `POST /api/security/roles/{id}/data-scopes`
- `DELETE /api/security/roles/{id}/data-scopes/{scopeId}`
- `POST /api/security/users/{id}/data-scope-overrides`
- `DELETE /api/security/users/{id}/data-scope-overrides/{scopeId}`

### Nhóm Policy Rule
- `GET /api/security/policy-rules`
- `POST /api/security/policy-rules`
- `PUT /api/security/policy-rules/{id}`
- `POST /api/security/policy-rules/{id}/activate`
- `POST /api/security/policy-rules/{id}/deactivate`

### Nhóm Delegation
- `GET /api/security/delegations`
- `POST /api/security/delegations`
- `PUT /api/security/delegations/{id}`
- `POST /api/security/delegations/{id}/cancel`

### Nhóm Audit
- `GET /api/security/audit/permission-changes`
- `GET /api/security/audit/access-decisions`
- `GET /api/security/audit/sensitive-actions`

## 5.4. Luồng kiểm tra quyền tại runtime

Khuyến nghị trình tự xử lý:

1. Xác thực user và nạp identity.
2. Lấy role trực tiếp của user.
3. Lấy role gián tiếp qua group.
4. Lấy delegation đang hiệu lực nếu có.
5. Tổng hợp permission.
6. Kiểm tra action/resource có được phép hay không.
7. Tính data scope hiệu lực.
8. Áp policy rule.
9. Ghi log quyết định allow/deny.
10. Thực thi hoặc trả lỗi 403.

## 5.5. Chuẩn lỗi trả về

Ví dụ response deny:

```json
{
  "code": "AUTH_FORBIDDEN",
  "message": "Người dùng không có quyền thực hiện thao tác này.",
  "details": {
    "resource": "HOSO",
    "action": "APPROVE",
    "reason": "Missing permission or invalid workflow state"
  }
}
```

---

## 6. Danh mục permission mẫu

### 6.1. Nhóm hồ sơ
- `HOSO.HOSO.VIEW`
- `HOSO.HOSO.CREATE`
- `HOSO.HOSO.UPDATE`
- `HOSO.HOSO.DELETE`
- `HOSO.HOSO.SUBMIT`
- `HOSO.HOSO.APPROVE`
- `HOSO.HOSO.REJECT`
- `HOSO.HOSO.EXPORT`

### 6.2. Nhóm danh mục
- `DM.TTHC.VIEW`
- `DM.TTHC.CREATE`
- `DM.TTHC.UPDATE`
- `DM.TTHC.DELETE`

### 6.3. Nhóm báo cáo
- `BC.BAOCAO.VIEW`
- `BC.BAOCAO.EXPORT`
- `BC.BAOCAO.APPROVE`

### 6.4. Nhóm quản trị
- `ADMIN.USER.VIEW`
- `ADMIN.USER.MANAGE`
- `ADMIN.ROLE.VIEW`
- `ADMIN.ROLE.MANAGE`
- `ADMIN.AUDIT.VIEW`
- `ADMIN.CONFIG.MANAGE`

---

## 7. Rủi ro thiết kế cần tránh

- chỉ ẩn menu ở FE nhưng không chặn ở API
- không có data scope
- role phát sinh theo từng cá nhân
- quá nhiều direct grant
- không có màn tra cứu effective permission
- không log before/after khi đổi quyền
- không kiểm soát quyền ở export/import/report
- để logic authorization rải rác trong controller/service mà không chuẩn hóa
- không có chiến lược cache/invalidate

---

## 8. Lộ trình triển khai đề xuất

### Giai đoạn 1
- user, group, role, permission
- menu + API authorization
- audit log cơ bản

### Giai đoạn 2
- data scope theo đơn vị và người tạo
- effective permission viewer
- delegation cơ bản

### Giai đoạn 3
- policy rule
- SoD
- phê duyệt thay đổi quyền nhạy cảm

### Giai đoạn 4
- rule engine nâng cao
- ABAC một phần nếu thực sự cần
- báo cáo phân tích quyền / phát hiện xung đột

---

## 9. Kết luận

Baseline phù hợp nhất cho hệ thống hành chính nhà nước là:

**RBAC + Data Scope + Policy Rule + Delegation + Audit**

Mô hình này cân bằng được:
- khả năng vận hành
- độ rõ ràng
- khả năng kiểm soát
- khả năng mở rộng
- yêu cầu tuân thủ và truy vết

Tài liệu này có thể dùng ngay làm baseline cho:
- tài liệu HLD/TKCT
- đặc tả use case
- thiết kế database
- thiết kế API
- dựng POC
- lập test case QC

