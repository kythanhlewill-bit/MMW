# ---- Build stage ----
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy solution + csproj trước để tận dụng cache khi restore
COPY MMW.sln ./
COPY src/MMW.Shared/MMW.Shared.csproj                 src/MMW.Shared/
COPY src/MMW.Domain/MMW.Domain.csproj                 src/MMW.Domain/
COPY src/MMW.Application/MMW.Application.csproj       src/MMW.Application/
COPY src/MMW.Infrastructure/MMW.Infrastructure.csproj src/MMW.Infrastructure/
COPY src/MMW.Web/MMW.Web.csproj                       src/MMW.Web/
RUN dotnet restore src/MMW.Web/MMW.Web.csproj

# Copy toàn bộ source và publish
COPY . .
RUN dotnet publish src/MMW.Web/MMW.Web.csproj -c Release -o /app/publish /p:UseAppHost=false

# ---- Runtime stage ----
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish ./

# .NET 8 mặc định lắng nghe cổng 8080 trong container
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080

ENTRYPOINT ["dotnet", "MMW.Web.dll"]
