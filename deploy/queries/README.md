# Truy vấn chẩn đoán

Truy vấn đọc-only chạy thẳng trên database `MMW`, dùng để trả lời những câu hỏi mà giao diện
chưa có sẵn. Không truy vấn nào ở đây ghi dữ liệu.

## Chạy

Trên VPS, qua container SQL Server dùng chung của YODES:

```bash
scp deploy/queries/<tên>.sql root@46.250.227.10:/tmp/q.sql
ssh root@46.250.227.10 'PW=$(grep "^MSSQL_SA_PASSWORD=" /opt/mmw/.env | cut -d= -f2-); \
  docker cp /tmp/q.sql yodes-db:/tmp/q.sql >/dev/null; \
  docker exec yodes-db /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$PW" -C -d MMW \
    -i /tmp/q.sql -W -s "|" -f 65001'
```

`-f 65001` là bắt buộc — không có nó thì nhãn tiếng Việt trong kết quả ra ký tự rác.

## Mốc cắt dữ liệu

Trước commit `35f6f24` (deploy **2026-08-11 13:46:15 UTC**), vòng chấm điểm thoát ngay khi gặp
veto cứng đầu tiên. Mọi phiếu bị veto sinh trước mốc đó mang dữ liệu **cụt**:

| Cột | Trước mốc | Sau mốc |
|---|---|---|
| `MarketScore`, `LiquidityScore` | luôn 0 (tiêu chí chưa được hỏi) | giá trị thật |
| `AvailableMaxPoints` | dừng ở chỗ veto, thường 8/85 | đủ 85/85 |
| `DataMultiplier` | ~0,09 — đọc nhầm thành mất nguồn dữ liệu | ~1,00 |
| Số dòng `EntryScorecardLines` | ~10 | ~22 |

Truy vấn nào đọc điểm thành phần của phiếu **bị veto** đều phải lọc từ mốc này trở đi. Cả hai
truy vấn dưới đây đã có biến `@cutoff` ở đầu file; đừng bỏ nó đi để "lấy thêm mẫu" — số cũ sẽ
kéo kết luận về đúng chiều ngược lại.

## Danh sách

| File | Trả lời câu gì |
|---|---|
| `htf-veto-shadow-score.sql` | Cổng HTF đang chặn những cơ hội mà engine tự chấm là chất lượng tới đâu |
| `htf-veto-forward-outcome.sql` | Chặn như vậy là **đúng hay sai** — mô phỏng giá sau đó từ kho nến 15m |

Hai truy vấn bổ nhau. Cái đầu đo *chất lượng theo đánh giá của chính engine*, cái sau đo *kết quả
thực tế*. Điểm bóng cao mà giá vẫn đi ngược nghĩa là cổng HTF đã cứu chứ không phải chặn nhầm —
chỉ đọc cái đầu sẽ kết luận sai.
