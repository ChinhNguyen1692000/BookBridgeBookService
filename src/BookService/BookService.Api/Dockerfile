# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy toàn bộ source của service
COPY . .

# Restore dependencies
RUN dotnet restore "src/BookService/BookService.Api/BookService.Api.csproj"

# Build app
RUN dotnet build "src/BookService/BookService.Api/BookService.Api.csproj" -c Release -o /app/build

# Stage 2: Publish
FROM build AS publish
RUN dotnet publish "src/BookService/BookService.Api/BookService.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Stage 3: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENV ASPNETCORE_URLS=http://+:${PORT}
ENTRYPOINT ["dotnet", "BookService.Api.dll"]
