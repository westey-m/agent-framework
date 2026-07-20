FROM python:3.12-slim

WORKDIR /app

COPY . user_agent/
WORKDIR /app/user_agent

RUN if [ -f requirements.txt ]; then \
        pip install -r requirements.txt; \
    else \
        echo "No requirements.txt found"; \
    fi

EXPOSE 8088

# The entrypoint for the hosted agent. Foundry builds this image and runs whatever CMD names, so
# this is what designates hosted.py (rather than console.py or evals.py) as the deployed host.
CMD ["python", "hosted.py"]
