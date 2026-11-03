FROM python:3.12-slim
WORKDIR /app
COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt
COPY cell_codec.py shape.py modules.py geometry.py native_mesh.py thumbnail.py registry.py service.py ./
COPY materialdefs.nqdef ./
COPY element-icons.json ./
RUN useradd --uid 10001 --create-home custom-elements && mkdir /data && chown 10001:10001 /data
USER 10001:10001
CMD ["python", "service.py"]
