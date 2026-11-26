# Build stage
FROM ://microsoft.com AS build
WORKDIR /src

# Copy csproj and restore
COPY ["PerceptAI.API.csproj", "./"]
RUN dotnet restore "PerceptAI.API.csproj"

# Copy everything and build
COPY . .
RUN dotnet publish "PerceptAI.API.csproj" -c Release -o /app/publish --no-restore

# Runtime stage
FROM ://microsoft.com AS final
WORKDIR /app

# Copy published app
COPY --from=build /app/publish .

# Download ML model from private GitHub repo (LFS)
ARG GITHUB_TOKEN
RUN apt-get update && apt-get install -y --no-install-recommends curl ca-certificates && \
    mkdir -p /ML /app/ML && \
    curl -fL -H "Authorization: token ${GITHUB_TOKEN}" \
         "https://githubusercontent.com" \
         -o /ML/model.onnx && \
    ln -s /ML/model.onnx /app/ML/model.onnx && \
    apt-get purge -y --auto-remove curl && \
    rm -rf /var/lib/apt/lists/*

# Expose port (Render sets PORT env var)
EXPOSE 8080

# Start
ENTRYPOINT ["dotnet", "PerceptAI.API.dll"]
