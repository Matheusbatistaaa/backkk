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

# Copy ONNX models from the repository instead of downloading with a token.
COPY --from=build /src/ML/*.onnx /app/ML/
COPY --from=build /src/ML/*.onnx.data /app/ML/

# Render sets PORT; Program.cs reads it and binds to 0.0.0.0.
ENV PORT=10000
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 10000

# Start
ENTRYPOINT ["dotnet", "PerceptAI.API.dll"]
