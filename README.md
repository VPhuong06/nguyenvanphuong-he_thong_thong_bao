# Hệ thống gửi thông báo hàng loạt

Bài tập lớn học phần **Phát triển ứng dụng phân tán** – Đề tài 11.

Hệ thống cho phép tạo chiến dịch thông báo tới nhiều người nhận. Campaign API chỉ tiếp nhận và tạo chiến dịch; mỗi thông báo được tách thành một job, đưa vào RabbitMQ và được nhiều Notification Worker xử lý bất đồng bộ.

## Kiến trúc

```
Admin Web ──REST──► Campaign API ──publish──► RabbitMQ ──consume──► Notification Worker (1..N)
                         │                                                  │
                         └──────────────────► PostgreSQL ◄──────────────────┘
```

- **Mẫu trao đổi thông điệp (MEP):** Qua hàng đợi (Queue-based). Một direct exchange `notify.exchange` và một queue chung `notify.jobs`; các worker là competing consumers nên mỗi job chỉ do một worker xử lý.
- Admin Web giao tiếp với hệ thống qua REST (Request–response).
- PostgreSQL là nguồn sự thật về trạng thái chiến dịch và từng job gửi.

## Cấu trúc thư mục

| Thư mục | Vai trò |
|---|---|
| `src/Campaign.Api` | ASP.NET Core Minimal API: tạo, xem, start chiến dịch, tra cứu kết quả |
| `src/Notification.Worker` | .NET Worker Service: nhận job từ RabbitMQ, gửi thông báo, ghi kết quả |
| `src/Admin.Web` | Blazor Web App: giao diện quản trị chiến dịch |
| `src/Notification.Shared` | Entity, `NotificationDbContext`, message contract, topology RabbitMQ |
| `docker-compose.yml` | Khởi chạy toàn bộ hệ thống |

## Yêu cầu

- Docker Desktop (Docker Compose v2)
- .NET SDK 10 (chỉ cần khi chạy hoặc build ngoài Docker)

## Chạy bằng Docker Compose

```bash
cp .env.example .env
docker compose up --build
```

Chạy nhiều worker:

```bash
docker compose up --build --scale notification-worker=3
```

| Thành phần | Địa chỉ |
|---|---|
| Campaign API | http://localhost:5000 |
| Admin Web | http://localhost:5001 |
| RabbitMQ Management | http://localhost:15672 (tài khoản trong `.env`) |
| PostgreSQL | localhost:5432 |

## Chạy ngoài Docker (phát triển)

Chỉ chạy hạ tầng bằng Docker, còn các service chạy bằng `dotnet run`:

```bash
docker compose up -d postgres rabbitmq
dotnet run --project src/Campaign.Api
dotnet run --project src/Notification.Worker
dotnet run --project src/Admin.Web
```

## Cấu hình

Cấu hình phụ thuộc môi trường được đọc từ biến môi trường (ghi đè `appsettings.json`):

| Biến | Ý nghĩa |
|---|---|
| `ConnectionStrings__Default` | Chuỗi kết nối PostgreSQL |
| `RabbitMq__Host`, `RabbitMq__Port` | Địa chỉ RabbitMQ |
| `RabbitMq__UserName`, `RabbitMq__Password` | Tài khoản RabbitMQ |
| `RabbitMq__PrefetchCount` | Số job tối đa mỗi worker giữ chưa ack |
| `CampaignApi__BaseUrl` | URL Campaign API mà Admin Web gọi tới |

Trong Docker, các container gọi nhau bằng tên service (`postgres`, `rabbitmq`, `campaign-api`).

## API

| Method | Path | Mô tả |
|---|---|---|
| POST | `/campaigns` | Tạo chiến dịch |
| GET | `/campaigns` | Danh sách chiến dịch |
| GET | `/campaigns/{id}` | Chi tiết và số lượng Pending/Sent/Failed |
| POST | `/campaigns/{id}/start` | Sinh job và đưa vào RabbitMQ |
| GET | `/campaigns/{id}/deliveries` | Kết quả gửi chi tiết |

## Tiến độ

- [x] Khung solution, model dữ liệu, cấu hình, logging, Docker
- [ ] Tạo / xem chiến dịch
- [ ] Start chiến dịch, publish job
- [ ] Worker xử lý job, ghi kết quả
- [ ] Chạy lại job thất bại
- [ ] Giao diện Admin Web
