#!/bin/bash
# Скрипт для автоматического обновления памяти проектов
# Добавьте в cron: 0 2 * * * /path/to/project/auto_update_memory.sh

echo "[AUTO UPDATE] $(date) - Starting automatic memory update"

# Переходим в директорию проекта
cd "$(dirname "$0")"

# Активируем виртуальное окружение (если используется)
# source venv/bin/activate

# Запускаем обновление
echo "[AUTO UPDATE] Running memory update..."
python update_memory_mcp.py

# Проверяем результат
if [ $? -eq 0 ]; then
    echo "[AUTO UPDATE] $(date) - Memory update completed successfully"
else
    echo "[AUTO UPDATE] $(date) - Memory update failed"
    # Здесь можно добавить отправку уведомления
fi

echo "[AUTO UPDATE] $(date) - Finished"
