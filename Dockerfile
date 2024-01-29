FROM registry.nexproject.ir/mcr/dotnet/sdk:7.0 AS build
WORKDIR /app

COPY . ./
RUN dotnet restore market.csproj

COPY . ./
RUN dotnet publish -c Release -o out market.csproj

FROM registry.nexproject.ir/mcr/dotnet/aspnet:7.0
WORKDIR /app
COPY --from=build /app/out .

EXPOSE 80

ENTRYPOINT ["dotnet", "market.dll"]
