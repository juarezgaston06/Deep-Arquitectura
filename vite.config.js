import { defineConfig } from 'vite';
import { resolve } from 'path';

export default defineConfig({
    build: {
        rollupOptions: {
            input: {
                main: resolve(__dirname, 'index.html'),
                electricidad: resolve(__dirname, 'electricidad/index.html'),
                gas: resolve(__dirname, 'gas/index.html'),
            },
        },
    },
});
