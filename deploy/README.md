# Deploy MMW lên VPS Contabo (dùng chung máy với YODES)

VPS: `46.250.227.10` (`vmi3433260.contaboserver.net`) — Ubuntu 24.04, 4 vCPU / 7.8 GB.
MMW chạy cạnh YODES, **dùng chung container SQL Server** `yodes-db` với database riêng tên `MMW`.

| | YODES | MMW |
|---|---|---|
| Thư mục | `/opt/yodes` | `/opt/mmw` |
| Container app | `yodes-web` | `mmw-web` |
| Cổng host | `127.0.0.1:8080` | `127.0.0.1:8081` |
| Database | `YODES` | `MMW` |
| Domain | `yodestarot.cloud` | `mmw.yodestarot.cloud` |

Container SQL Server `yodes-db` là của compose project `yodes`. MMW **không** khai báo service `db`,
chỉ gắn vào mạng `yodes_default` và gọi tới alias `db`.

## 0. Chuẩn bị (làm trên máy local)

Trỏ DNS: thêm bản ghi `A` cho `mmw.yodestarot.cloud` → `46.250.227.10`, đợi phân giải xong.

Commit + push code lên `origin/main` — bước 2 sẽ clone từ GitHub.

## 1. Bật swap (khuyến nghị)

VPS đang có **0 swap**. Thêm MMW nghĩa là thêm ~500 MB thường trực và các đợt tăng vọt khi
build .NET SDK image. Không có swap thì OOM killer sẽ giết thẳng container — có thể trúng `yodes-db`.

```bash
fallocate -l 2G /swapfile && chmod 600 /swapfile && mkswap /swapfile && swapon /swapfile
echo '/swapfile none swap sw 0 0' >> /etc/fstab
```

## 2. Lấy source về VPS

```bash
git clone https://github.com/kythanhlewill-bit/MMW.git /opt/mmw
```

## 3. Tạo file .env

```bash
cd /opt/mmw && cp .env.example .env
```

Chép mật khẩu SA từ YODES sang (không cần đọc/gõ tay mật khẩu):

```bash
sed -i "/^MSSQL_SA_PASSWORD=/d" /opt/mmw/.env && grep '^MSSQL_SA_PASSWORD=' /opt/yodes/.env >> /opt/mmw/.env
```

Sau đó mở `/opt/mmw/.env` điền nốt: `BOOTSTRAP_ADMIN_PASSWORD`, `AI_API_KEY`,
và `BINANCE_API_KEY`/`BINANCE_API_SECRET` nếu cần đồng bộ lệnh.

Khoá quyền đọc:

```bash
chmod 600 /opt/mmw/.env
```

## 4. Build và chạy

`MigrateAsync()` trong `SeedData` tự tạo database `MMW` và áp toàn bộ migration khi container
khởi động — không cần chạy `dotnet ef` thủ công.

```bash
cd /opt/mmw && docker compose up -d --build
```

Theo dõi lần khởi động đầu (migration + seed mất vài chục giây):

```bash
docker logs -f mmw-web
```

## 5. Nginx + chứng chỉ

```bash
cp /opt/mmw/deploy/nginx-mmw.conf /etc/nginx/sites-available/mmw
ln -sf /etc/nginx/sites-available/mmw /etc/nginx/sites-enabled/mmw
nginx -t && systemctl reload nginx
```

Cấp chứng chỉ (Certbot tự sửa file conf để thêm khối 443):

```bash
certbot --nginx -d mmw.yodestarot.cloud
```

## 6. Kiểm tra

```bash
curl -I https://mmw.yodestarot.cloud
```

Vào `https://mmw.yodestarot.cloud` đăng nhập bằng `BOOTSTRAP_ADMIN_USER`, rồi mở `/hangfire`
xem 12 recurring job đã đăng ký và đang chạy chưa.

## Cập nhật code về sau

```bash
cd /opt/mmw && git pull && docker compose up -d --build
```

## Gỡ bỏ sau tuần chạy thử

```bash
cd /opt/mmw && docker compose down -v
docker rmi mmw-app
rm -f /etc/nginx/sites-enabled/mmw && nginx -t && systemctl reload nginx
```

Database `MMW` vẫn nằm trong volume `mssql-data` của YODES. Xoá hẳn:

```bash
docker exec -it yodes-db /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C \
  -Q "ALTER DATABASE MMW SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE MMW;"
```
