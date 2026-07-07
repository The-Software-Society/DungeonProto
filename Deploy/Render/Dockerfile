FROM ubuntu:24.04

RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates libstdc++6 \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY server/ /app/
COPY start.sh /app/start.sh

RUN chmod +x /app/start.sh \
    && find /app -maxdepth 1 -type f -name "*.x86_64" -exec chmod +x {} \;

ENV PORT=10000
EXPOSE 10000

CMD ["/app/start.sh"]
