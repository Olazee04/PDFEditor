FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY PDFEditor.csproj .
RUN dotnet restore

COPY . .
RUN dotnet publish -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# Install libgdiplus for PDF rendering support
RUN apt-get update && apt-get install -y \
    libgdiplus \
    libc6-dev \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

# Create writable directories
RUN mkdir -p /app/wwwroot/uploads /app/wwwroot/versions

EXPOSE 8080
ENV PORT=8080
ENV ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["dotnet", "PDFEditor.dll"]
