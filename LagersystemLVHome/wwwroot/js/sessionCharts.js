// Session Management Charts
window.SessionCharts = {
    deviceChart: null,
    riskChart: null,

    renderDeviceChart(labels, data) {
        const ctx = document.getElementById("deviceChart");
        if (!ctx) return;

        if (this.deviceChart) {
            this.deviceChart.destroy();
        }

        this.deviceChart = new Chart(ctx, {
            type: "doughnut",
            data: {
                labels: labels,
                datasets: [
                    {
                        data: data,
                        backgroundColor: [
                            "#4f46e5", // indigo
                            "#10b981", // green
                            "#f59e0b", // amber
                            "#ef4444", // red
                            "#8b5cf6", // purple
                        ],
                    },
                ],
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                plugins: {
                    legend: {
                        position: "bottom",
                    },
                },
            },
        });
    },

    renderRiskChart(labels, data) {
        const ctx = document.getElementById("riskChart");
        if (!ctx) return;

        if (this.riskChart) {
            this.riskChart.destroy();
        }

        const colorMap = {
            VeryLow: "#10b981",
            Low: "#3b82f6",
            Medium: "#f59e0b",
            High: "#f97316",
            Critical: "#ef4444",
        };

        const backgroundColors = labels.map(
            (label) => colorMap[label] || "#6b7280",
        );

        this.riskChart = new Chart(ctx, {
            type: "bar",
            data: {
                labels: labels,
                datasets: [
                    {
                        label: "Sessions",
                        data: data,
                        backgroundColor: backgroundColors,
                    },
                ],
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                scales: {
                    y: {
                        beginAtZero: true,
                        ticks: {
                            stepSize: 1,
                        },
                    },
                },
                plugins: {
                    legend: {
                        display: false,
                    },
                },
            },
        });
    },

    destroyCharts() {
        if (this.deviceChart) {
            this.deviceChart.destroy();
            this.deviceChart = null;
        }
        if (this.riskChart) {
            this.riskChart.destroy();
            this.riskChart = null;
        }
    },
};
