# PROPOSAL-2025-0081: Исправление расположения диагностического кода

## Диагностика
При анализе логов и текущего кода `charts/server.py` была обнаружена ошибка в моей предыдущей реализации. Диагностический код для логирования потребления памяти и размера данных был ошибочно помещен в блок `except`, который никогда не выполняется при штатном завершении работы функции. Из-за этого мы не получаем необходимые нам данные для подтверждения гипотезы о сбое по нехватке памяти.

## Предлагаемое изменение
Предлагается переместить блок диагностического кода из `except` в конец блока `try`, непосредственно перед формированием и отправкой ответа.

### Изменения в `charts/server.py`
```python
<<<<<<< SEARCH
:start_line:171
-------
        valid_results = [res for res in results if res is not None]
        logging.info(f"Successfully processed {len(valid_results)} pairs.")

        response_data = {"charts_data": valid_results}
        logging.info("Successfully created response data. Sending to client.")
        return response_data

    except Exception as e:
        logging.error(f"An unhandled exception occurred in get_dashboard_data: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"An internal server error occurred: {str(e)}")
        try:
            # Логируем размер данных
            data_size_mb = sys.getsizeof(valid_results) / (1024 * 1024)
            logging.info(f"Size of final 'valid_results' list: {data_size_mb:.2f} MB")

            # Логируем использование памяти процессом
            process = psutil.Process(os.getpid())
            memory_mb = process.memory_info().rss / (1024 * 1024)
            logging.info(f"Current memory usage before serialization: {memory_mb:.2f} MB")
        except Exception as mem_e:
            logging.error(f"Could not get memory/size info: {mem_e}")
=======
        valid_results = [res for res in results if res is not None]
        logging.info(f"Successfully processed {len(valid_results)} pairs.")

        try:
            # Логируем размер данных
            data_size_mb = sys.getsizeof(valid_results) / (1024 * 1024)
            logging.info(f"Size of final 'valid_results' list: {data_size_mb:.2f} MB")

            # Логируем использование памяти процессом
            process = psutil.Process(os.getpid())
            memory_mb = process.memory_info().rss / (1024 * 1024)
            logging.info(f"Current memory usage before serialization: {memory_mb:.2f} MB")
        except Exception as mem_e:
            logging.error(f"Could not get memory/size info: {mem_e}")

        response_data = {"charts_data": valid_results}
        logging.info("Successfully created response data. Sending to client.")
        return response_data

    except Exception as e:
        logging.error(f"An unhandled exception occurred in get_dashboard_data: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"An internal server error occurred: {str(e)}")
>>>>>>> REPLACE
```

## Обоснование
Это исправление гарантирует, что диагностический код будет выполнен в каждой успешной сессии, что позволит нам получить необходимые данные о потреблении памяти и размере ответа непосредственно перед потенциальным сбоем.

## Оценка рисков
Риск отсутствует. Это исправление логической ошибки в отладочном коде.

## План тестирования
1.  Перезапустить сервер `charts`.
2.  Воспроизвести сценарий с большим количеством пар.
3.  Проанализировать лог сервера. Теперь в нем должны появиться строки `Size of final 'valid_results' list:` и `Current memory usage before serialization:` перед сообщением `Successfully created response data`.

## План отката
1.  Отменить изменения в файле `charts/server.py`.