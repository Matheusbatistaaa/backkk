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

# Download ML models from private GitHub repo (LFS)
ARG GITHUB_TOKEN
RUN apt-get update && apt-get install -y --no-install-recommends curl ca-certificates && \
    mkdir -p /app/ML && \
    SUB="api" && \
    API="${SUB}.github.com" && \
    curl -fL -H "Authorization: Bearer ${GITHUB_TOKEN}" \
         -H "Accept: application/vnd.github.raw+json" \
         "https://${API}/repos/Matheusbatistaaa/backkk/contents/ML/perceptai_physio.onnx?ref=main" \
         -o /app/ML/perceptai_physio.onnx && \
    curl -fL -H "Authorization: Bearer ${GITHUB_TOKEN}" \
         -H "Accept: application/vnd.github.raw+json" \
         "https://${API}/repos/Matheusbatistaaa/backkk/contents/ML/perceptai_emotion.onnx?ref=main" \
         -o /app/ML/perceptai_emotion.onnx && \
    curl -fL -H "Authorization: Bearer ${GITHUB_TOKEN}" \
         -H "Accept: application/vnd.github.raw+json" \
         "https://${API}/repos/Matheusbatistaaa/backkk/contents/ML/perceptai_mobilenetv2.onnx.data?ref=main" \
         -o /app/ML/perceptai_mobilenetv2.onnx.data && \
    apt-get purge -y --auto-remove curl && \
    rm -rf /var/lib/apt/lists/*

# Render sets PORT; Program.cs reads it and binds to 0.0.0.0.
ENV PORT=10000
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 10000

# Start
ENTRYPOINT ["dotnet", "PerceptAI.API.dll"]
