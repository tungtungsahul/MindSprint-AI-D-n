# Ảnh chạy MindSprint AI trên server Linux (xem docs/TRIEN_KHAI_SERVER.md).
# Bước 1: biên dịch + đóng gói migration bằng bộ SDK; bước 2: chỉ chép kết quả sang ảnh runtime nhỏ hơn.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY dotnet-tools.json ./
RUN dotnet tool restore
COPY MindSprintAI_ASP.NET_MVC/MindSprintAI_ASP.NET_MVC.csproj MindSprintAI_ASP.NET_MVC/
RUN dotnet restore MindSprintAI_ASP.NET_MVC/MindSprintAI_ASP.NET_MVC.csproj
COPY MindSprintAI_ASP.NET_MVC/ MindSprintAI_ASP.NET_MVC/
RUN dotnet publish MindSprintAI_ASP.NET_MVC/MindSprintAI_ASP.NET_MVC.csproj -c Release -o /app --no-restore \
 && rm -f /app/appsettings.Development.json
# efbundle: một tệp chạy được, áp các migration còn thiếu vào database (dùng ở bước "cập nhật database" của hướng dẫn).
RUN dotnet ef migrations bundle --project MindSprintAI_ASP.NET_MVC --configuration Release --output /app/efbundle

FROM mcr.microsoft.com/dotnet/aspnet:10.0
# Bài làm theo đề: đổi đề Word sang PDF bằng LibreOffice không giao diện (giống deploy/setup-mindsprint.sh); phông đo chữ
# giống Times New Roman/Arial/Calibri/Cambria để đề giữ bố cục.
RUN apt-get update \
 && apt-get install -y --no-install-recommends libreoffice-writer-nogui fonts-liberation2 fonts-crosextra-carlito fonts-crosextra-caladea fonts-dejavu-core \
 && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .
# App_Data giữ khóa mã hóa (key Gemini của người dùng, phiên đăng nhập), nhật ký: nằm trên ổ đĩa riêng (volume) để không mất khi cập nhật.
RUN mkdir -p /app/App_Data && chown -R app:app /app/App_Data
USER app
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
ENTRYPOINT ["dotnet", "MindSprintAI_ASP.NET_MVC.dll"]
