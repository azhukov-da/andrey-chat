FROM python:3.12-slim

WORKDIR /app

COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

COPY app.py .

ENV STT_MODEL_SIZE=base
ENV STT_DEVICE=cpu
ENV STT_COMPUTE_TYPE=int8

EXPOSE 9000

CMD ["uvicorn", "app:app", "--host", "0.0.0.0", "--port", "9000"]
