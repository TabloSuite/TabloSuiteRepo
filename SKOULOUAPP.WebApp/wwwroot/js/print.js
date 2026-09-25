window.SKOULOUAPPPrint = {
    open: () => window.setTimeout(() => window.print(), 0),
    openElement: (selector, title) => {
        const source = document.querySelector(selector);
        if (!source) {
            return false;
        }

        const printWindow = window.open('', '_blank', 'width=1200,height=1400');
        if (!printWindow) {
            return false;
        }

        const contentHtml = source.innerHTML;
        const styles = `
            <style>
                @page { size: auto; }
                * { box-sizing: border-box; }
                html, body { margin: 0; padding: 0; background: #fff; color: #0f172a; font-family: Inter, Arial, sans-serif; -webkit-print-color-adjust: exact; print-color-adjust: exact; }
                body { display: flex; justify-content: center; align-items: flex-start; padding: 0; }
                .pdf-print-shell { width: 100%; margin: 0 auto; }
                .pdf-print-shell > div { width: 100%; border: 0; border-radius: 0; padding: 8px; margin: 0 auto; }

                @media screen {
                    html, body { min-height: 100vh; }
                    .pdf-print-shell { min-height: 100vh; }
                    .pdf-print-shell > div { min-height: calc(100vh - 4mm); }
                }

                @media print {
                    .pdf-print-shell,
                    .pdf-print-shell > div {
                        min-height: 0 !important;
                    }
                }

                p, h1 { margin: 0; }
                .text-xs { font-size: 11px; }
                .text-sm { font-size: 13px; }
                .text-base { font-size: 16px; }
                .text-lg { font-size: 20px; }
                .text-xl { font-size: 24px; }
                .text-2xl { font-size: 24px; }
                .text-3xl { font-size: 30px; }
                .font-bold { font-weight: 700; }
                .font-black { font-weight: 800; }
                .font-extrabold { font-weight: 800; }
                .uppercase { text-transform: uppercase; }

                .text-indigo-700 { color: #4338ca; }
                .text-emerald-700 { color: #047857; }
                .text-rose-700 { color: #be123c; }
                .text-slate-900 { color: #0f172a; }
                .text-slate-600 { color: #475569; }
                .text-slate-500 { color: #64748b; }

                .mt-2 { margin-top: 8px; }
                .mt-1 { margin-top: 4px; }
                .mt-3 { margin-top: 12px; }
                .mt-5 { margin-top: 20px; }
                .py-4 { padding-top: 16px; padding-bottom: 16px; }
                .px-3 { padding-left: 12px; padding-right: 12px; }
                .py-2 { padding-top: 8px; padding-bottom: 8px; }

                .grid { display: grid; }
                .grid-cols-3 { grid-template-columns: repeat(3, 1fr); }
                .gap-3 { gap: 12px; }
                .border-y { border-top: 1px solid #e2e8f0; border-bottom: 1px solid #e2e8f0; }

                .rounded-xl { border-radius: 12px; }
                .bg-slate-50 { background: #f8fafc; }

                table { width: 100%; border-collapse: collapse; margin-top: 20px; }
                thead tr { border-bottom: 1px solid #cbd5e1; }
                tbody tr { border-bottom: 1px solid #f1f5f9; }
                th, td { padding: 8px 12px; text-align: left; }
                th { font-size: 11px; text-transform: uppercase; color: #64748b; }
                td.text-right, th.text-right { text-align: right; }
                .font-semibold { font-weight: 600; }
            </style>`;

        printWindow.document.open();
        printWindow.document.write('<!doctype html><html><head><meta charset="utf-8"><title>' + (title || 'PDF') + '</title>' + styles + '</head><body><div class="pdf-print-shell">' + contentHtml + '</div></body></html>');
        printWindow.document.close();

        printWindow.onload = () => {
            printWindow.focus();
            printWindow.print();
            printWindow.close();
        };

        return true;
    }
};
