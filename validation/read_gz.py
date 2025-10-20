import gzip
import sys

def read_gz_csv_first_last(file_path):
    """Reads a gzipped CSV file and prints the first and last data lines."""
    try:
        with gzip.open(file_path, 'rt', encoding='utf-8') as f:
            header = f.readline().strip()
            print(f"File: {file_path}")
            print(f"Header: {header}")

            first_line = None
            last_line = None

            for line in f:
                if first_line is None:
                    first_line = line.strip()
                last_line = line.strip()

            if first_line:
                print(f"First data entry: {first_line}")
            else:
                print("No data entries found after header.")

            if last_line and last_line != first_line:
                print(f"Last data entry:  {last_line}")
            elif last_line:
                print("Only one data entry found.")

    except FileNotFoundError:
        print(f"Error: File not found at {file_path}")
    except Exception as e:
        print(f"An error occurred: {e}")

if __name__ == "__main__":
    if len(sys.argv) > 1:
        file_to_read = sys.argv[1]
        read_gz_csv_first_last(file_to_read)
    else:
        print("Please provide a file path as an argument.")
