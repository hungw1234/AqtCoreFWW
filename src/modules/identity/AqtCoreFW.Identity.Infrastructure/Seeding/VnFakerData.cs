namespace AqtCoreFW.Identity.Infrastructure.Seeding;

/// <summary>
/// Vietnamese fake-name datasets for Bogus Faker.
/// Generated name format: "{Surname} {MidName} {FirstName}" (VN ordering).
/// Combinations: 100 surnames × 50 mid × 300 first ≈ 1.5M unique, covers 10k target w/out dup.
/// </summary>
public static class VnFakerData
{
    /// <summary>Top ~100 Vietnamese surnames (holand) by frequency.</summary>
    public static readonly string[] Surnames =
    {
        "Nguyễn", "Trần", "Lê", "Phạm", "Hoàng", "Huỳnh", "Phan", "Vũ", "Võ", "Đặng",
        "Bùi", "Đỗ", "Hồ", "Ngô", "Dương", "Lý", "Mai", "Đinh", "Trịnh", "Cao",
        "Lương", "Lâm", "Đoàn", "Tô", "Vương", "Đào", "Tạ", "Thái", "Hà", "Trương",
        "Nghiêm", "Tăng", "Hạ", "Khúc", "Kim", "Bạch", "Châu", "Đồng", "Giang", "Chu",
        "Lại", "Khổng", "Trịnh", "Tôn", "Thân", "Quách", "Lưu", "Trần", "Kiều", "Ngụy",
        "Phùng", "Hàn", "Hứa", "Cù", "Diệp", "Triệu", "Ninh", "Vân", "Viên", "Doãn",
        "Thành", "Phó", "Đậu", "Quan", "Tần", "Tiêu", "Tiền", "Uông", "Sầm", "Nhan",
        "Chung", "Thạch", "Lư", "Lương", "Nông", "Ông", "Thi", "Trà", "Lộc", "Sơn",
        "Sử", "Từ", "Tạ", "Vi", "Viễn", "Xa", "Xuân", "Yến", "Âu", "Ân",
        "Ấm", "Bảo", "Hậu", "Khánh", "Lôi", "Miên", "Phước", "Tùng", "Ung", "Đàm"
    };

    /// <summary>Common Vietnamese mid-names (tên đệm).</summary>
    public static readonly string[] MidNames =
    {
        "Văn", "Thị", "Hữu", "Đức", "Minh", "Quốc", "Anh", "Bá", "Khánh", "Tiến",
        "Thanh", "Thành", "Thu", "Thuỷ", "Xuân", "Hạ", "Đông", "Phương", "Đình", "Ngọc",
        "Kim", "Quý", "Quang", "Tuấn", "Công", "Gia", "Nhật", "Phú", "Phi", "Hồng",
        "Trọng", "Mạnh", "Đại", "Duy", "Hải", "Hùng", "Khắc", "Lan", "Lệ", "Lợi",
        "Mai", "Mỹ", "Nam", "Như", "Phước", "Quỳnh", "Sinh", "Tâm", "Thái", "Tuệ"
    };

    /// <summary>Common Vietnamese first-names (tên chính).</summary>
    public static readonly string[] FirstNames =
    {
        "An", "Anh", "Bảo", "Bình", "Châu", "Chi", "Công", "Cường", "Dũng", "Duy",
        "Đạt", "Đức", "Giang", "Hà", "Hải", "Hạnh", "Hằng", "Hiếu", "Hoa", "Hoài",
        "Hoàng", "Hồng", "Huệ", "Hùng", "Huy", "Khang", "Khánh", "Khoa", "Khôi", "Kiên",
        "Kiệt", "Lan", "Lâm", "Linh", "Loan", "Lộc", "Long", "Lưu", "Ly", "Mai",
        "Minh", "My", "Nam", "Nga", "Ngân", "Ngọc", "Nguyên", "Nhân", "Nhật", "Nhi",
        "Oanh", "Phát", "Phong", "Phú", "Phúc", "Phương", "Quân", "Quang", "Quốc", "Quỳnh",
        "Sang", "Sơn", "Tài", "Tâm", "Tân", "Thắng", "Thành", "Thảo", "Thiện", "Thịnh",
        "Thu", "Thuận", "Thuỷ", "Thy", "Tiến", "Toàn", "Trang", "Trí", "Trinh", "Trung",
        "Trường", "Tú", "Tuấn", "Tuyết", "Uyên", "Vân", "Việt", "Vinh", "Vũ", "Xuân",
        "Yến", "Ân", "Bích", "Cẩm", "Diệu", "Đan", "Gia", "Hiền", "Hương", "Hữu",
        "Khả", "Khiêm", "Khôi", "Lập", "Liêm", "Liên", "Mạnh", "Mậu", "Mỹ", "Nghĩa",
        "Nhã", "Nhiên", "Phú", "Phi", "Quảng", "Quý", "Sáng", "Tín", "Trâm", "Vĩ",
        "Viễn", "Xuyên", "Yên", "Bảo", "Chính", "Diệp", "Dương", "Đăng", "Định", "Giáp",
        "Hào", "Hận", "Huyền", "Khôi", "Kiêm", "Lãm", "Lâm", "Lập", "Lệ", "Liễu",
        "Luân", "Mai", "Mão", "Nam", "Ngân", "Nghi", "Nguyệt", "Nhã", "Phi", "Phước",
        "Quả", "Quyên", "Sâm", "Sương", "Tấn", "Thái", "Thoa", "Thương", "Thụy", "Tiên",
        "Tiệp", "Tiểu", "Trác", "Trâm", "Trường", "Tuệ", "Tuyên", "Tường", "Viên", "Vương",
        "Đăng", "Bằng", "Cát", "Cúc", "Dần", "Đình", "Đỗ", "Giao", "Hạ", "Hảo",
        "Hào", "Hiên", "Hiệp", "Hòa", "Hoạch", "Hoan", "Hợi", "Hùng", "Hưng", "Hiệp",
        "Khải", "Khang", "Khanh", "Khoa", "Kiêm", "Kiều", "Lài", "Lân", "Lập", "Lĩnh",
        "Luận", "Lương", "Mẫn", "Mậu", "Minh", "Mộc", "Mẫn", "Mậu", "Nam", "Ngọ",
        "Nhân", "Nhiên", "Ninh", "Oai", "Pháp", "Phong", "Quản", "Quí", "Quế", "Quyết",
        "Sinh", "Tài", "Tấn", "Tiếp", "Tích", "Tiên", "Toản", "Trác", "Trình", "Triệu",
        "Trọng", "Trúc", "Tùng", "Tưởng", "Tuy", "Văn", "Viễn", "Việt", "Vinh", "Vũ",
        "Vượng", "Xuân", "Yến", "Ánh", "Bảo", "Bích", "Cẩn", "Cát", "Cẩm", "Chánh",
        "Diễn", "Diệu", "Đạo", "Đằng", "Đệ", "Điền", "Định", "Đoán", "Đông", "Đồng",
        "Giang", "Hà", "Hạ", "Hân", "Hào", "Hi", "Hiên", "Hiệp", "Hoàng", "Hợp",
        "Khả", "Khiêm", "Kiến", "Kinh", "Lạc", "Lai", "Lập", "Lĩnh", "Lộc", "Luận",
        "Lương", "Mậu", "Mộc", "Nam", "Nghiễm", "Nguyên", "Nhượng", "Ninh", "Oai", "Phát"
    };
}
