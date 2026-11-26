# Build stage
ARG REG=mcr
ARG HOST=microsoft.com
FROM ${REG}.${HOST}/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy csproj and restore
COPY ["PerceptAI.API.csproj", "./"]
RUN dotnet restore "PerceptAI.API.csproj"

# Copy everything and build
COPY . .
RUN dotnet publish "PerceptAI.API.csproj" -c Release -o /app/publish --no-restore

# Runtime stage
ARG REG=mcr
ARG HOST=microsoft.com
FROM ${REG}.${HOST}/dotnet/aspnet:9.0 AS final
WORKDIR /app

# Copy published app
COPY --from=build /app/publish .

# Download ML model from private GitHub repo (LFS)
ARG GITHUB_TOKEN
RUN apt-get update && apt-get install -y --no-install-recommends curl ca-certificates && \
    mkdir -p /ML /app/ML && \
    SUB="media" && DOM="githubusercontent.com" && \
    curl -fL -H "Authorization: token ${GITHUB_TOKEN}" \
         "https://${SUB}.${DOM}/media/Matheusbatistaaa/backkk/main/ML/model.onnx" \
         -o /ML/model.onnx && \
    ln -s /ML/model.onnx /app/ML/model.onnx && \
    apt-get purge -y --auto-remove curl && \
    rm -rf /var/lib/apt/lists/*

# Expose port (Render sets PORT env var)
EXPOSE 8080

# Start
ENTRYPOINT ["dotnet", "PerceptAI.API.dll"]
